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
$applicationLicensePath = Join-Path $repositoryRoot 'LICENSE'
$thirdPartyNoticePath = Join-Path $repositoryRoot 'THIRD-PARTY-NOTICES.txt'
$commonLicenseDirectory = Join-Path $repositoryRoot 'licenses'
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

$repositoryLegalFiles = @(
    $applicationLicensePath,
    $thirdPartyNoticePath,
    (Join-Path $commonLicenseDirectory 'MIT.txt'),
    (Join-Path $commonLicenseDirectory 'Apache-2.0.txt')
)

foreach ($legalFile in $repositoryLegalFiles) {
    if (-not (Test-Path -LiteralPath $legalFile -PathType Leaf)) {
        throw "Required legal file was not found: $legalFile"
    }
}

$publishedLicenseDirectory = Join-Path $publishDirectory 'licenses'
New-Item -ItemType Directory -Path $publishedLicenseDirectory -Force | Out-Null

Copy-Item -LiteralPath $applicationLicensePath -Destination (Join-Path $publishDirectory 'LICENSE.txt')
Copy-Item -LiteralPath $thirdPartyNoticePath -Destination (Join-Path $publishDirectory 'THIRD-PARTY-NOTICES.txt')
Copy-Item -LiteralPath (Join-Path $commonLicenseDirectory 'MIT.txt') -Destination $publishedLicenseDirectory
Copy-Item -LiteralPath (Join-Path $commonLicenseDirectory 'Apache-2.0.txt') -Destination $publishedLicenseDirectory

$dotnetCommand = Get-Command dotnet -ErrorAction Stop
$dotnetRoot = Split-Path -Parent $dotnetCommand.Source
$dotnetLicensePath = Join-Path $dotnetRoot 'LICENSE.txt'
$dotnetNoticesPath = Join-Path $dotnetRoot 'ThirdPartyNotices.txt'

foreach ($dotnetLegalFile in @($dotnetLicensePath, $dotnetNoticesPath)) {
    if (-not (Test-Path -LiteralPath $dotnetLegalFile -PathType Leaf)) {
        throw "The .NET installation legal file was not found: $dotnetLegalFile"
    }
}

Copy-Item -LiteralPath $dotnetLicensePath -Destination (Join-Path $publishedLicenseDirectory 'DotNet-LICENSE.txt')
Copy-Item -LiteralPath $dotnetNoticesPath -Destination (Join-Path $publishedLicenseDirectory 'DotNet-THIRD-PARTY-NOTICES.txt')

$globalPackagesOutput = dotnet nuget locals global-packages --list --force-english-output
if ($LASTEXITCODE -ne 0) {
    throw "Could not determine the NuGet global packages directory."
}

$globalPackagesLine = $globalPackagesOutput |
    Where-Object { $_ -like 'global-packages:*' } |
    Select-Object -First 1

if (-not $globalPackagesLine) {
    throw "The NuGet global packages directory was not present in dotnet nuget locals output."
}

$globalPackagesPath = $globalPackagesLine.Substring($globalPackagesLine.IndexOf(':') + 1).Trim()
$publishedDepsPath = Join-Path $publishDirectory 'EntityTracker.Wpf.deps.json'
$publishedDependencies = Get-Content -LiteralPath $publishedDepsPath -Raw | ConvertFrom-Json
$nativeNoticePackageNames = $publishedDependencies.libraries.PSObject.Properties.Name |
    Where-Object { $_ -match '^(SkiaSharp|HarfBuzzSharp)\.NativeAssets\.Win32/' }

if (-not $nativeNoticePackageNames) {
    throw "No published SkiaSharp or HarfBuzzSharp Windows native package was found for notice collection."
}

$nativeNoticeSources = foreach ($packageName in $nativeNoticePackageNames) {
    $packageId, $packageVersion = $packageName -split '/', 2
    $packageNoticePath = Join-Path $globalPackagesPath (
        Join-Path $packageId.ToLowerInvariant() (
            Join-Path $packageVersion.ToLowerInvariant() 'THIRD-PARTY-NOTICES.txt'))

    if (-not (Test-Path -LiteralPath $packageNoticePath -PathType Leaf)) {
        throw "The native package notice was not found: $packageNoticePath"
    }

    [PSCustomObject]@{
        Path = $packageNoticePath
        Hash = (Get-FileHash -LiteralPath $packageNoticePath -Algorithm SHA256).Hash
    }
}

$distinctNativeNoticeHashes = @($nativeNoticeSources.Hash | Sort-Object -Unique)
if ($distinctNativeNoticeHashes.Count -ne 1) {
    throw "SkiaSharp and HarfBuzzSharp ship different native notices. Review and update the packaging script before publishing."
}

Copy-Item `
    -LiteralPath $nativeNoticeSources[0].Path `
    -Destination (Join-Path $publishedLicenseDirectory 'SkiaSharp-HarfBuzzSharp-THIRD-PARTY-NOTICES.txt')

Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $zipPath

Write-Host "EntityTracker package created: $zipPath"
