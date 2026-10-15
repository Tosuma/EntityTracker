[CmdletBinding()]
param(
    # Also delete your EntityTracker data: the database (every Project and Tracker on this computer),
    # settings, backups and logs. Without it the data stays, and a later install picks it up again.
    [switch]$RemoveData,

    # Also delete the Docker volume that keeps NuGet packages between builds (entitytracker-nuget),
    # and any build volume an interrupted install left (entitytracker-build-*).
    [switch]$RemoveDockerCache,

    # Don't ask for confirmation.
    [switch]$Force,

    # Where the app and data live; only changed by tests.
    [string]$LocalAppDataPath = [Environment]::GetFolderPath('LocalApplicationData'),
    [string]$StartMenuPath = (Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Microsoft\Windows\Start Menu\Programs')
)

# Removes EntityTracker as Install-EntityTracker.ps1 installed it: the app folder, its Start Menu
# shortcut and the build folders older versions of the updater left. The source clone is never touched.
. (Join-Path $PSScriptRoot 'Install-Common.ps1')
$ErrorActionPreference = 'Stop'

$installParent = Join-Path $LocalAppDataPath 'Programs'
$installPath = Join-Path $installParent 'EntityTracker'
$dataPath = Join-Path $LocalAppDataPath 'EntityTracker'
$updaterPath = Join-Path $dataPath 'updater'
$shortcutPath = Join-Path $StartMenuPath 'EntityTracker.lnk'

function Confirm-Step {
    param([string]$Question)
    if ($Force) { return $true }
    $answer = Read-Host "$Question [y/N]"
    return $answer -match '^(y|yes)$'
}

function Remove-Folder {
    param([string]$Parent, [string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return }
    Assert-ChildPath $Parent $Path
    Remove-Item -LiteralPath $Path -Recurse -Force
    Write-Host "Removed $Path"
}

# The app can't be removed while it, or an update of it, is running from the install folder.
$installFull = [IO.Path]::GetFullPath($installPath).TrimEnd('\') + '\'
$running = @(Get-Process -Name 'EntityTracker.Wpf' -ErrorAction SilentlyContinue | Where-Object {
        $path = $null
        try { $path = $_.Path } catch { }
        $path -and $path.StartsWith($installFull, [StringComparison]::OrdinalIgnoreCase)
    })
if ($running.Count -gt 0) {
    throw 'EntityTracker is running. Close it, then run the uninstall again.'
}

$leftovers = @(if (Test-Path -LiteralPath $installParent) {
        Get-ChildItem -LiteralPath $installParent -Directory -Filter 'EntityTracker.*' |
            Where-Object { $_.Name -match '^EntityTracker\.(new|old)\.[0-9a-f]{32}$' } |
            ForEach-Object { $_.FullName }
    })
$installed = (Test-Path -LiteralPath $installPath) -or (Test-Path -LiteralPath $shortcutPath) -or $leftovers.Count -gt 0

if ($installed) {
    if (-not (Confirm-Step "Remove EntityTracker from $installPath and the Start Menu?")) {
        Write-Host 'Nothing was removed.'
        exit 1
    }
    Remove-Folder $installParent $installPath
    foreach ($leftover in $leftovers) { Remove-Folder $installParent $leftover }
    if (Test-Path -LiteralPath $shortcutPath) {
        Remove-Item -LiteralPath $shortcutPath -Force
        Write-Host "Removed $shortcutPath"
    }
}
else {
    Write-Host "EntityTracker is not installed in $installPath."
}
# Build folders left by an interrupted install or update of an older version, which built on Windows.
Remove-Folder $dataPath $updaterPath

if ($RemoveData) {
    if (Test-Path -LiteralPath $dataPath) {
        Write-Warning "This deletes every Project and Tracker stored on this computer, with its settings, backups and logs, in $dataPath. Projects linked to a Git repository can be imported again from it; anything else is gone."
        if (Confirm-Step 'Delete your EntityTracker data?') {
            Remove-Folder $LocalAppDataPath $dataPath
        }
        else {
            Write-Host "Your data was kept in $dataPath."
        }
    }
}
elseif (Test-Path -LiteralPath $dataPath) {
    Write-Host "Your data was kept in $dataPath; installing EntityTracker again picks it up. To delete it too, run the uninstall with -RemoveData."
}

if ($RemoveDockerCache) {
    if (Get-Command docker -ErrorAction SilentlyContinue) {
        $buildVolumes = Get-DockerOutput @('volume', 'ls', '--quiet', '--filter', 'name=entitytracker-build-') -Quiet
        $volumes = @('entitytracker-nuget') + @($buildVolumes.Lines | Where-Object { $_ -match '^entitytracker-build-[0-9a-f]{32}$' })
        foreach ($volume in $volumes) {
            $removed = Get-DockerOutput @('volume', 'rm', $volume)
            if ($removed.ExitCode -eq 0) { Write-Host "Removed the Docker volume $volume." }
            else { Write-Warning "The Docker volume $volume could not be removed (Docker may not be running, or it is already gone)." }
        }
    }
    else {
        Write-Warning 'Docker was not found, so the Docker volume entitytracker-nuget was not removed.'
    }
    Write-Host 'The .NET SDK and Git images are left in Docker, since other tools may use them. List them with: docker image ls mcr.microsoft.com/dotnet/sdk; docker image ls alpine/git'
}

Write-Host 'EntityTracker was uninstalled. Your source clone was not changed; delete it yourself if you no longer need it.'
exit 0
