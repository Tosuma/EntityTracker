[CmdletBinding()]
# The app passes -UpdaterVersion and -UpdaterSource to the copy of this script from the release
# it installs, so future versions of this script must keep accepting both parameters.
param(
    [Parameter(Mandatory)][string]$SourcePath,
    [Parameter(Mandatory)][string]$Tag,
    [Parameter(Mandatory)][int]$WaitForProcess,
    [string]$UpdaterVersion = '',
    [ValidateSet('', 'Release', 'Installed')][string]$UpdaterSource = ''
)

# The app starts this in a console window after it closes. It runs the installer in the same
# console, so Docker's progress shows as it happens, then reopens the app and closes itself. On a
# failure it stays open and offers a retry.
$ErrorActionPreference = 'Stop'

# Which version of the updater scripts is running, shown here and in the installer's first line.
$scriptVersion = if (-not $UpdaterVersion) { 'unknown version' }
    elseif ($UpdaterSource -eq 'Release') { "$UpdaterVersion (from the release being installed)" }
    elseif ($UpdaterSource -eq 'Installed') { "$UpdaterVersion (installed with the current app)" }
    else { $UpdaterVersion }

# Tests run the updater with redirected input; it then never waits for a key.
$interactive = -not [Console]::IsInputRedirected
try { $Host.UI.RawUI.WindowTitle = "EntityTracker update to $Tag" } catch { }

Write-Host "Updating EntityTracker to $Tag" -ForegroundColor Cyan
Write-Host "Updater $scriptVersion"

$installer = Join-Path $PSScriptRoot 'Install-EntityTracker.ps1'
# A trailing backslash would escape the closing quote Windows PowerShell adds around the path.
$SourcePath = [IO.Path]::GetFullPath((Join-Path $SourcePath '.'))
$installArguments = @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', $installer,
    '-SourcePath', $SourcePath, '-Tag', $Tag, '-WaitForProcess', $WaitForProcess,
    '-UpdaterVersion', $scriptVersion)

while ($true) {
    Write-Host ''
    # A separate Windows PowerShell keeps the installer's errors and exit code its own, and its
    # output goes straight to this console.
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & powershell.exe @installArguments; $code = $LASTEXITCODE }
    finally { $ErrorActionPreference = $previous }

    Write-Host ''
    if ($code -eq 0) {
        Write-Host "Update complete: EntityTracker $Tag is starting. This window closes in 5 seconds." -ForegroundColor Green
        if ($interactive) { Start-Sleep -Seconds 5 }
        exit 0
    }

    Write-Host "The update failed (exit code $code). The installed app was not changed." -ForegroundColor Red
    if (-not $interactive) { exit $code }
    Write-Host 'Press R to retry, any other key to exit.'
    $key = [Console]::ReadKey($true)
    if ($key.Key -ne [ConsoleKey]::R) { exit $code }
    # The app has closed by now, so the retry doesn't wait for it again.
    $installArguments[[array]::IndexOf($installArguments, '-WaitForProcess') + 1] = 0
}
