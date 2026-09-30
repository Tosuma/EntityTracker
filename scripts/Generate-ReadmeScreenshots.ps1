[CmdletBinding()]
param(
    [switch]$UpdateReadme,
    [string]$Output,
    [string]$Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$arguments = @(
    'run',
    '--project',
    (Join-Path $repositoryRoot 'tools\EntityTracker.Screenshots\EntityTracker.Screenshots.csproj'),
    '--configuration',
    'Release',
    "-p:Version=$Version",
    "-p:InformationalVersion=$Version"
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
