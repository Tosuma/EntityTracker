[CmdletBinding()]
param()

# Checks that the installer reports which version of the scripts is running.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$installer = Join-Path $PSScriptRoot 'Install-EntityTracker.ps1'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('EntityTracker-installer-version-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null

function Invoke-Installer {
    param([string[]]$Arguments)
    # Windows PowerShell, as the updater uses; the empty folder is not a source checkout, so the
    # installer stops right after announcing its version.
    $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $installer -SourcePath $testRoot @Arguments 2>&1
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Lines = @($output | ForEach-Object { "$_" }) }
}

try {
    $passed = Invoke-Installer @('-UpdaterVersion', 'app-v9.9.9 (from the release being installed)')
    if ($passed.Lines[0] -ne 'EntityTracker installer app-v9.9.9 (from the release being installed)') {
        throw "The installer did not announce the version it was given: '$($passed.Lines[0])'."
    }
    if ($passed.ExitCode -eq 0) { throw 'The installer accepted a folder that is not a source checkout.' }

    $byHand = Invoke-Installer @()
    if ($byHand.Lines[0] -notmatch '^EntityTracker installer \S+ \(source checkout\)$') {
        throw "The installer did not read its version from the checkout: '$($byHand.Lines[0])'."
    }

    # Test-Updater.ps1 checks that the updater hands its version to the installer.
    Write-Host 'Installer version tests passed.'
    # The installer runs above exit non-zero on purpose; without this the leftover $LASTEXITCODE
    # fails a CI step that otherwise passed.
    exit 0
}
finally {
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}
