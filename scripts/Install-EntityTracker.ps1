[CmdletBinding()]
param(
    [string]$SourcePath = (Split-Path -Parent $PSScriptRoot),
    [string]$Tag,
    [int]$WaitForProcess = 0,
    # Set by the updater; when run by hand the version is read from the source checkout.
    [string]$UpdaterVersion = '',

    # Where the app is installed and its shortcut created; only changed by tests.
    [string]$LocalAppDataPath = [Environment]::GetFolderPath('LocalApplicationData'),
    [string]$StartMenuPath = (Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Microsoft\Windows\Start Menu\Programs'),
    # Install without starting the app; used by tests.
    [switch]$NoStart
)

# Installs an approved release with Docker: a .NET SDK container fetches the release tag from the source
# clone's origin and checks it, the .NET SDK container the release pins builds it, and the same
# image saves the build next to the install folder. Windows then swaps it in, creates the Start Menu
# shortcut and starts the app. Nothing is built or checked out on this computer itself.
. (Join-Path $PSScriptRoot 'Install-Common.ps1')
$ErrorActionPreference = 'Stop'

if (-not $UpdaterVersion -and (Get-Command git -ErrorAction SilentlyContinue)) {
    try {
        $described = & git -C $PSScriptRoot describe --tags --match 'app-v*' --always --dirty 2>$null
        if ($LASTEXITCODE -eq 0 -and $described) { $UpdaterVersion = "$described (source checkout)" }
    }
    catch { }
}
if (-not $UpdaterVersion) { $UpdaterVersion = 'unknown version' }
Write-Host "EntityTracker installer $UpdaterVersion"

$SourcePath = [IO.Path]::GetFullPath($SourcePath)
$originUrl = ConvertTo-AnonymousUrl (Get-OriginUrl $SourcePath)
if ($Tag -and $Tag -notmatch '^app-v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
    throw 'The release tag must be app-vX.Y.Z.'
}

$installParent = Join-Path $LocalAppDataPath 'Programs'
$installPath = Join-Path $installParent 'EntityTracker'
$runId = [guid]::NewGuid().ToString('N')
$newName = "EntityTracker.new.$runId"
$newInstall = Join-Path $installParent $newName
$oldInstall = Join-Path $installParent "EntityTracker.old.$runId"
foreach ($path in @($newInstall, $oldInstall, $installPath)) { Assert-ChildPath $installParent $path }
# The release's source and build live in this volume, removed when the install ends.
$workVolume = "entitytracker-build-$runId"

function Write-Step {
    param([int]$Number, [string]$Text)
    Write-Host ''
    Write-Host "[$Number/5] $Text" -ForegroundColor Cyan
}

try {
    Write-Step 1 'Checking Docker'
    Assert-DockerReady
    Assert-DockerImage $script:FetchImage ".NET SDK $script:FetchSdkVersion (about 1 GB)"

    $releaseName = if ($Tag) { $Tag } else { 'the newest release' }
    Write-Step 2 "Fetching $releaseName from $originUrl"
    $release = Select-ReleaseTag (Get-ReleaseRefs $originUrl) $Tag
    $Tag = $release.Tag
    $version = $Tag.Substring('app-v'.Length)
    $sdkVersion = Invoke-ReleaseFetch $originUrl $release $workVolume
    Write-Host "  Fetched $Tag; it builds with .NET SDK $sdkVersion."

    Write-Step 3 "Building EntityTracker $version"
    Invoke-ContainerBuild -SdkVersion $sdkVersion -SourceMount "${workVolume}:/work" `
        -WorkingDirectory '/work/source' -Version $version

    Write-Step 4 "Saving the app to $installParent"
    $manifest = [pscustomobject]@{ SourcePath = $SourcePath; Version = $Tag } | ConvertTo-Json
    Save-ContainerBuild $sdkVersion $workVolume $installParent $newName $manifest
    if (-not (Test-Path -LiteralPath (Join-Path $newInstall 'EntityTracker.Wpf.exe') -PathType Leaf)) {
        throw "Docker saved the app, but it is not in $newInstall. Allow Docker Desktop or Rancher Desktop to share this drive, then run the install or update again."
    }

    Write-Step 5 "Installing EntityTracker $Tag"
    if ($WaitForProcess -gt 0) {
        $oldProcess = Get-Process -Id $WaitForProcess -ErrorAction SilentlyContinue
        if ($oldProcess) {
            Write-Host '  Waiting for EntityTracker to close...'
            $oldProcess.WaitForExit()
        }
    }

    Invoke-InstallSwap $installParent $installPath $newInstall $oldInstall {
        param($installedPath)
        $executable = Join-Path $installedPath 'EntityTracker.Wpf.exe'
        New-Item -ItemType Directory -Path $StartMenuPath -Force | Out-Null
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut((Join-Path $StartMenuPath 'EntityTracker.lnk'))
        $shortcut.TargetPath = $executable
        $shortcut.WorkingDirectory = $installedPath
        $shortcut.IconLocation = $executable + ',0'
        $shortcut.Save()
        if (-not $NoStart) { Start-Process -FilePath $executable -WorkingDirectory $installedPath }
    }
    Write-Host "  Installed EntityTracker $Tag at $installPath" -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $newInstall) {
        Assert-ChildPath $installParent $newInstall
        Remove-Item -LiteralPath $newInstall -Recurse -Force -ErrorAction SilentlyContinue
    }
    if (Get-Command docker -ErrorAction SilentlyContinue) {
        $null = Get-DockerOutput @('volume', 'rm', '--force', $workVolume) -Quiet
    }
}

exit 0
