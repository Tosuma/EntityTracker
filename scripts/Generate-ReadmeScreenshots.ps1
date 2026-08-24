[CmdletBinding()]
param(
    [switch]$UpdateReadme,
    [string]$Output
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$arguments = @(
    'run',
    '--project',
    (Join-Path $repositoryRoot 'tools\EntityTracker.Screenshots\EntityTracker.Screenshots.csproj'),
    '--configuration',
    'Release'
)

if ($UpdateReadme) {
    if ($Output) {
        throw 'Use either -Output or -UpdateReadme, not both.'
    }

    $arguments += @('--', '--update-readme')
}
elseif ($Output) {
    $arguments += @('--', '--output', $Output)
}

& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "README screenshot generation failed with exit code $LASTEXITCODE."
}
