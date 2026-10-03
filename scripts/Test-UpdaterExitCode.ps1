[CmdletBinding()]
param()

# The updater runs under Windows PowerShell 5.1, where the lost-exit-code bug lives, so the test
# re-runs itself there when started from PowerShell 7 (as CI does).
if ($PSVersionTable.PSEdition -ne 'Desktop') {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath
    exit $LASTEXITCODE
}

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Load the real Start-InstallWorker function from the updater without opening its window.
$updaterPath = Join-Path $PSScriptRoot 'Update-EntityTracker.ps1'
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($updaterPath, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors) { throw "Update-EntityTracker.ps1 does not parse: $($parseErrors[0].Message)" }
$definition = $ast.Find({ param($node)
        $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
        $node.Name -eq 'Start-InstallWorker' }, $true)
if (-not $definition) { throw 'Update-EntityTracker.ps1 has no Start-InstallWorker function.' }
. ([scriptblock]::Create($definition.Extent.Text))

$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('EntityTracker-updater-exit-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
try {
    $fakeInstall = Join-Path $testRoot 'Install-EntityTracker.ps1'
    Set-Content -LiteralPath $fakeInstall -Value @(
        'param([string]$SourcePath, [string]$Tag, [int]$WaitForProcess, [int]$ExitWith)',
        'Write-Host "Installed EntityTracker $Tag"',
        'exit $ExitWith')

    foreach ($expected in @(0, 3)) {
        $worker = Start-InstallWorker -InstallScript $fakeInstall `
            -InstallArguments @('-SourcePath', ('"' + $testRoot + '"'), '-Tag', 'app-v9.9.9',
                '-WaitForProcess', '0', '-ExitWith', $expected) `
            -WorkingDirectory $testRoot `
            -StdoutPath (Join-Path $testRoot "out-$expected.log") `
            -StderrPath (Join-Path $testRoot "err-$expected.log")
        # Poll like the updater's timer instead of waiting on the process.
        $deadline = [DateTime]::UtcNow.AddSeconds(60)
        while (-not $worker.HasExited) {
            if ([DateTime]::UtcNow -gt $deadline) { throw 'The fake install did not finish.' }
            Start-Sleep -Milliseconds 100
        }
        $code = $worker.ExitCode
        if ($null -eq $code -or $code -ne $expected) {
            throw "The updater read exit code [$code] for an install that exited with $expected."
        }
        if (-not (Get-Content -LiteralPath (Join-Path $testRoot "out-$expected.log") -Raw).Contains('Installed EntityTracker app-v9.9.9')) {
            throw 'The updater did not capture the install output.'
        }
    }

    Write-Host 'Updater exit code tests passed.'
}
finally {
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}
