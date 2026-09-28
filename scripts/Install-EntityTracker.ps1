[CmdletBinding()]
param(
    [string]$SourcePath = (Split-Path -Parent $PSScriptRoot),
    [string]$Tag,
    [int]$WaitForProcess = 0
)

. (Join-Path $PSScriptRoot 'Install-Common.ps1')
$ErrorActionPreference = 'Stop'
$env:GIT_TERMINAL_PROMPT = '0'
$env:GCM_INTERACTIVE = 'never'

$SourcePath = [IO.Path]::GetFullPath($SourcePath)
$actualRoot = (& git -C $SourcePath rev-parse --show-toplevel 2>$null)
if ($LASTEXITCODE -ne 0 -or -not $actualRoot -or
    [IO.Path]::GetFullPath($actualRoot).TrimEnd('\', '/') -ne $SourcePath.TrimEnd('\', '/')) {
    throw 'SourcePath must be the root of an existing app source checkout.'
}

if (-not $Tag) { $Tag = Get-StableReleaseTag $SourcePath }
if ($Tag -notmatch '^app-v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
    throw 'The release tag must be app-vX.Y.Z.'
}
$version = "$($Matches[1]).$($Matches[2]).$($Matches[3])"

$remoteRef = & git -C $SourcePath ls-remote --refs --tags origin "refs/tags/$Tag"
if ($LASTEXITCODE -ne 0 -or @($remoteRef).Count -ne 1 -or
    $remoteRef -notmatch "^[0-9a-fA-F]{40}\s+refs/tags/$([regex]::Escape($Tag))$") {
    throw "The approved release tag $Tag was not found on origin."
}
$remoteHash = ($remoteRef -split '\s+')[0]
Invoke-RequiredCommand git @('-C', $SourcePath, 'fetch', '--no-tags', 'origin',
    "refs/tags/${Tag}:refs/tags/${Tag}")
$localHash = & git -C $SourcePath rev-parse "refs/tags/$Tag"
if ($LASTEXITCODE -ne 0 -or $localHash -ne $remoteHash) {
    throw "The local $Tag tag differs from origin. Refusing to install."
}

$localData = [Environment]::GetFolderPath('LocalApplicationData')
$installParent = Join-Path $localData 'Programs'
$installPath = Join-Path $installParent 'EntityTracker'
$dataPath = Join-Path $localData 'EntityTracker'
$workRoot = Join-Path $dataPath ('updater\' + [guid]::NewGuid().ToString('N'))
$archivePath = Join-Path $workRoot 'source.tar'
$sourceStage = Join-Path $workRoot 'source'
$newInstall = Join-Path $installParent ('EntityTracker.new.' + [guid]::NewGuid().ToString('N'))
$oldInstall = Join-Path $installParent ('EntityTracker.old.' + [guid]::NewGuid().ToString('N'))
foreach ($path in @($workRoot, $sourceStage)) { Assert-ChildPath $dataPath $path }
foreach ($path in @($newInstall, $oldInstall, $installPath)) { Assert-ChildPath $installParent $path }
New-Item -ItemType Directory -Path $sourceStage -Force | Out-Null
New-Item -ItemType Directory -Path $installParent -Force | Out-Null

try {
    Write-Host "Preparing approved release $Tag..."
    Invoke-RequiredCommand git @('-C', $SourcePath, 'archive', "--output=$archivePath", $Tag)
    Invoke-RequiredCommand tar @('-xf', $archivePath, '-C', $sourceStage)
    Write-Host "Building EntityTracker $version locally..."
    & (Join-Path $sourceStage 'scripts\Publish-Windows.ps1') -Version $version
    if ($LASTEXITCODE -ne 0) { throw 'The app build failed.' }
    $publish = Join-Path $sourceStage 'artifacts\publish\win-x64'
    if (-not (Test-Path -LiteralPath (Join-Path $publish 'EntityTracker.Wpf.exe') -PathType Leaf)) {
        throw 'The app build did not produce EntityTracker.Wpf.exe.'
    }
    Copy-Item -LiteralPath $publish -Destination $newInstall -Recurse
    [pscustomobject]@{ SourcePath = $SourcePath; Version = $Tag } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $newInstall 'app-install.json') -Encoding UTF8

    if ($WaitForProcess -gt 0) {
        Write-Host 'Waiting for EntityTracker to close...'
        $oldProcess = Get-Process -Id $WaitForProcess -ErrorAction SilentlyContinue
        if ($oldProcess) { $oldProcess.WaitForExit() }
    }

    $movedOld = $false
    try {
        if (Test-Path -LiteralPath $installPath) {
            Move-Item -LiteralPath $installPath -Destination $oldInstall
            $movedOld = $true
        }
        Move-Item -LiteralPath $newInstall -Destination $installPath
        $shortcutDirectory = Join-Path ([Environment]::GetFolderPath('ApplicationData'))
            'Microsoft\Windows\Start Menu\Programs'
        New-Item -ItemType Directory -Path $shortcutDirectory -Force | Out-Null
        $shortcutPath = Join-Path $shortcutDirectory 'EntityTracker.lnk'
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($shortcutPath)
        $shortcut.TargetPath = Join-Path $installPath 'EntityTracker.Wpf.exe'
        $shortcut.WorkingDirectory = $installPath
        $shortcut.IconLocation = (Join-Path $installPath 'EntityTracker.Wpf.exe') + ',0'
        $shortcut.Save()
        Start-Process -FilePath (Join-Path $installPath 'EntityTracker.Wpf.exe')
    }
    catch {
        if (Test-Path -LiteralPath $installPath) {
            Assert-ChildPath $installParent $installPath
            Remove-Item -LiteralPath $installPath -Recurse -Force
        }
        if ($movedOld) { Move-Item -LiteralPath $oldInstall -Destination $installPath }
        throw
    }

    if ($movedOld) {
        Assert-ChildPath $installParent $oldInstall
        try { Remove-Item -LiteralPath $oldInstall -Recurse -Force }
        catch { Write-Warning "The previous install could not be removed: $oldInstall" }
    }
    Write-Host "Installed EntityTracker $Tag at $installPath"
}
finally {
    if (Test-Path -LiteralPath $newInstall) {
        Assert-ChildPath $installParent $newInstall
        Remove-Item -LiteralPath $newInstall -Recurse -Force
    }
    if (Test-Path -LiteralPath $workRoot) {
        Assert-ChildPath $dataPath $workRoot
        Remove-Item -LiteralPath $workRoot -Recurse -Force
    }
}
