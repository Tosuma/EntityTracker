[CmdletBinding()]
param()

# Checks Uninstall-EntityTracker.ps1 against a fake install in a temporary folder: what it removes,
# that data stays unless asked for, that it refuses while the app is running, and its Docker clean-up.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot 'Uninstall-EntityTracker.ps1'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('EntityTracker-uninstall-' + [guid]::NewGuid().ToString('N'))
$localData = Join-Path $testRoot 'LocalAppData'
$startMenu = Join-Path $testRoot 'StartMenu'
$install = Join-Path $localData 'Programs\EntityTracker'
$data = Join-Path $localData 'EntityTracker'
$shortcut = Join-Path $startMenu 'EntityTracker.lnk'
$leftover = Join-Path $localData ('Programs\EntityTracker.old.' + [guid]::NewGuid().ToString('N'))
$other = Join-Path $localData 'Programs\SomethingElse'

function New-FakeInstall {
    New-Item -ItemType Directory -Path $install, $leftover, $other, (Join-Path $data 'updater\abc'), $startMenu -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $install 'EntityTracker.Wpf.exe') -Value 'app'
    Set-Content -LiteralPath (Join-Path $data 'entity-tracker.db') -Value 'data'
    Set-Content -LiteralPath $shortcut -Value 'shortcut'
}

function Invoke-Uninstall {
    param([string[]]$Arguments)
    # Windows PowerShell, which is what a colleague most likely runs it in.
    $ErrorActionPreference = 'Continue'
    try {
        $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $script -LocalAppDataPath $localData `
            -StartMenuPath $startMenu @Arguments 2>&1
    }
    finally { $ErrorActionPreference = 'Stop' }
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Text = ($output | ForEach-Object { "$_" }) -join "`n" }
}

function Assert-Exists { param([string]$Path, [bool]$Expected, [string]$What)
    if ((Test-Path -LiteralPath $Path) -ne $Expected) { throw "$What should $(if ($Expected) { 'still exist' } else { 'be gone' }): $Path" }
}

try {
    # A plain uninstall removes the app, its leftovers, the shortcut and updater folders, and keeps the data.
    New-FakeInstall
    $result = Invoke-Uninstall @('-Force')
    if ($result.ExitCode -ne 0) { throw "The uninstall failed: $($result.Text)" }
    Assert-Exists $install $false 'The app'
    Assert-Exists $leftover $false 'An old install left by an update'
    Assert-Exists $shortcut $false 'The Start Menu shortcut'
    Assert-Exists (Join-Path $data 'updater') $false 'The updater build folder'
    Assert-Exists (Join-Path $data 'entity-tracker.db') $true 'The database'
    Assert-Exists $other $true 'Another program in the Programs folder'
    if ($result.Text -notlike '*Your data was kept*') { throw "The uninstall did not say the data was kept: $($result.Text)" }

    # -RemoveData deletes the data too.
    $result = Invoke-Uninstall @('-Force', '-RemoveData')
    if ($result.ExitCode -ne 0) { throw "The uninstall with -RemoveData failed: $($result.Text)" }
    Assert-Exists $data $false 'The data folder'

    # It refuses while the app runs from the install folder (a copy of ping stands in for the app).
    New-FakeInstall
    $fakeApp = Join-Path $install 'EntityTracker.Wpf.exe'
    Copy-Item -LiteralPath (Join-Path $env:SystemRoot 'System32\PING.EXE') -Destination $fakeApp -Force
    $process = Start-Process -FilePath $fakeApp -ArgumentList '-n', '60', '127.0.0.1' -WindowStyle Hidden -PassThru
    try {
        $result = Invoke-Uninstall @('-Force')
        if ($result.ExitCode -eq 0 -or $result.Text -notlike '*EntityTracker is running*') {
            throw "The uninstall did not refuse while the app was running: $($result.Text)"
        }
        Assert-Exists $install $true 'The app (while running)'
    }
    finally { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue; $process.WaitForExit() }

    # -RemoveDockerCache removes the NuGet volume and the build volumes interrupted installs left,
    # and nothing else (docker's name filter matches anywhere in the name).
    $fakeBin = Join-Path $testRoot 'bin'
    $dockerLog = Join-Path $testRoot 'docker.log'
    $buildVolume = 'entitytracker-build-' + [guid]::NewGuid().ToString('N')
    New-Item -ItemType Directory -Path $fakeBin -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $fakeBin 'fake-docker.ps1') -Value @(
        'Add-Content -LiteralPath $env:FAKE_DOCKER_LOG -Value (($args | ForEach-Object { "[$_]" }) -join " ")',
        "if (`$args[1] -eq 'ls') { Write-Output '$buildVolume'; Write-Output 'other-entitytracker-build-volume' }",
        'exit 0')
    Set-Content -LiteralPath (Join-Path $fakeBin 'docker.cmd') -Value '@powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0fake-docker.ps1" %*'
    $originalPath = $env:PATH
    $env:PATH = "$fakeBin;$originalPath"
    $env:FAKE_DOCKER_LOG = $dockerLog
    try { $result = Invoke-Uninstall @('-Force', '-RemoveDockerCache') }
    finally { $env:PATH = $originalPath; Remove-Item env:FAKE_DOCKER_LOG }
    if ($result.ExitCode -ne 0) { throw "The uninstall with -RemoveDockerCache failed: $($result.Text)" }
    $removed = @(Get-Content -LiteralPath $dockerLog | Where-Object { $_.StartsWith('[volume] [rm]') })
    if (($removed -join '; ') -ne "[volume] [rm] [entitytracker-nuget]; [volume] [rm] [$buildVolume]") {
        throw "The uninstall removed the wrong Docker volumes: $($removed -join '; ')"
    }

    Write-Host 'Uninstall tests passed.'
    exit 0
}
finally {
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}
