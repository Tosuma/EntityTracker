[CmdletBinding()]
param()

# Runs Update-EntityTracker.ps1 under Windows PowerShell, as the app starts it, next to a fake
# installer: the updater must pass its arguments on, show the installer's output, and report the
# install's exit code. Input is redirected, so the updater neither waits nor asks to retry.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('EntityTracker-updater-' + [guid]::NewGuid().ToString('N'))
$staging = Join-Path $testRoot 'updater staging'
$source = Join-Path $testRoot 'source clone'
New-Item -ItemType Directory -Path $staging, $source | Out-Null
try {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Update-EntityTracker.ps1') -Destination $staging
    Set-Content -LiteralPath (Join-Path $staging 'Install-EntityTracker.ps1') -Value @(
        'param([string]$SourcePath, [string]$Tag, [int]$WaitForProcess, [string]$UpdaterVersion)',
        'Write-Host "Fake install of $Tag from [$SourcePath] after process $WaitForProcess"',
        'Write-Host "EntityTracker installer $UpdaterVersion"',
        'exit [int]$env:FAKE_INSTALL_EXIT')

    foreach ($expected in @(0, 3)) {
        $env:FAKE_INSTALL_EXIT = $expected
        $ErrorActionPreference = 'Continue'
        try {
            # A trailing backslash must not break the quoting of the path passed on.
            $output = '' | & powershell.exe -NoProfile -ExecutionPolicy Bypass `
                -File (Join-Path $staging 'Update-EntityTracker.ps1') -SourcePath "$source\" -Tag 'app-v9.9.9' `
                -WaitForProcess 0 -UpdaterVersion 'app-v9.9.9' -UpdaterSource 'Release' 2>&1
        }
        finally { $ErrorActionPreference = 'Stop' }
        $code = $LASTEXITCODE
        $text = ($output | ForEach-Object { "$_" }) -join "`n"
        if ($code -ne $expected) { throw "The updater exited with [$code] for an install that exited with $expected`: $text" }
        foreach ($line in @("Fake install of app-v9.9.9 from [$source] after process 0",
                            'EntityTracker installer app-v9.9.9 (from the release being installed)')) {
            if (-not $text.Contains($line)) { throw "The updater output is missing '$line': $text" }
        }
        $result = if ($expected -eq 0) { 'Update complete: EntityTracker app-v9.9.9 is starting.' }
            else { 'The update failed (exit code 3).' }
        if (-not $text.Contains($result)) { throw "The updater did not report '$result': $text" }
    }

    Write-Host 'Updater tests passed.'
    exit 0
}
finally {
    Remove-Item env:FAKE_INSTALL_EXIT -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}
