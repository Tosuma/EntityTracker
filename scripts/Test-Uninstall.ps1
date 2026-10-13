[CmdletBinding()]
param()

# Checks Uninstall-EntityTracker.ps1 against a fake install in a temporary folder: what it removes,
# that data stays unless asked for, and that it refuses while the app is running.
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

    Write-Host 'Uninstall tests passed.'
    exit 0
}
finally {
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}
