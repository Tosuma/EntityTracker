[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$artifactRoot = Join-Path $repositoryRoot 'artifacts'
$publishDirectory = Join-Path $artifactRoot 'publish\win-x64'
$zipPath = Join-Path $artifactRoot 'EntityTracker-win-x64.zip'
$projectPath = Join-Path $repositoryRoot 'src\EntityTracker.Wpf\EntityTracker.Wpf.csproj'
$artifactRootFullPath = [System.IO.Path]::GetFullPath($artifactRoot)
$publishDirectoryFullPath = [System.IO.Path]::GetFullPath($publishDirectory)

if (-not $publishDirectoryFullPath.StartsWith(
        $artifactRootFullPath + [System.IO.Path]::DirectorySeparatorChar,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to replace a publish directory outside the repository artifacts folder."
}

if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

dotnet publish $projectPath `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    --output $publishDirectory `
    -p:DebugType=None `
    -p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $zipPath

Write-Host "EntityTracker package created: $zipPath"
