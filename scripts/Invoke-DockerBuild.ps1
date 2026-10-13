[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$SourcePath,

    [string]$Version
)

# Builds EntityTracker from a staged source folder inside the official .NET SDK container, so the
# machine needs Docker (Docker Desktop or Rancher Desktop) but no .NET. The container writes the
# build to <SourcePath>\artifacts\publish\win-x64, exactly where a local build puts it; the installer
# installs it from there as usual. The container is removed afterwards.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$dockerHelp = 'EntityTracker builds in Docker. Start Docker Desktop or Rancher Desktop (with the Docker ' +
    'engine and Linux containers), then run the install or update again.'

# Docker writes progress to stderr. Windows PowerShell 5.1 turns that into errors that would stop this
# script, so docker runs with errors allowed and only its exit code decides.
function Invoke-Docker {
    param([string[]]$Arguments)
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & docker @Arguments 2>&1 | ForEach-Object { Write-Host $_ } }
    finally { $ErrorActionPreference = $previous }
    return $LASTEXITCODE
}

function Get-DockerOutput {
    param([string[]]$Arguments)
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $output = & docker @Arguments 2>$null }
    finally { $ErrorActionPreference = $previous }
    return [pscustomobject]@{ Output = "$output".Trim(); ExitCode = $LASTEXITCODE }
}

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw "Docker was not found. $dockerHelp"
}

$sourceFull = [IO.Path]::GetFullPath($SourcePath)
if (-not (Test-Path -LiteralPath (Join-Path $sourceFull 'global.json') -PathType Leaf)) {
    throw "The staged source has no global.json: $sourceFull"
}

$info = Get-DockerOutput @('info', '--format', '{{.OSType}}')
if ($info.ExitCode -ne 0) {
    throw "Docker is not running. $dockerHelp"
}
if ($info.Output -ne 'linux') {
    throw "Docker is set to Windows containers. Switch it to Linux containers, then run the install or update again."
}

# Build with the SDK this release pins, so every release builds the way it was tested.
$sdkVersion = (Get-Content -LiteralPath (Join-Path $sourceFull 'global.json') -Raw | ConvertFrom-Json).sdk.version
if ("$sdkVersion" -notmatch '^\d+\.\d+\.\d+$') {
    throw "global.json does not pin an exact .NET SDK version: $sdkVersion"
}
$image = "mcr.microsoft.com/dotnet/sdk:$sdkVersion"

if ((Get-DockerOutput @('image', 'inspect', '--format', '{{.Id}}', $image)).ExitCode -ne 0) {
    Write-Host "Downloading the .NET SDK $sdkVersion build image. The first time takes a while; later updates reuse it."
    if ((Invoke-Docker @('pull', $image)) -ne 0) {
        throw "Could not download $image. Check the internet connection and that Docker is running."
    }
}

$mount = "${sourceFull}:/src"

# Check that Docker can write to the folder before spending time on the build.
$probe = Join-Path $sourceFull '.docker-share-check'
if (Test-Path -LiteralPath $probe) { Remove-Item -LiteralPath $probe -Force }
$probeExit = Invoke-Docker @('run', '--rm', '-v', $mount, $image, 'sh', '-c', 'echo ok > /src/.docker-share-check')
if ($probeExit -ne 0 -or -not (Test-Path -LiteralPath $probe -PathType Leaf)) {
    throw "Docker could not write to $sourceFull. Allow Docker Desktop or Rancher Desktop to share this drive, then run the install or update again."
}
Remove-Item -LiteralPath $probe -Force

Write-Host "Building EntityTracker in Docker ($image)..."
$publishArguments = @('-NoProfile', '-NonInteractive', '-File', 'scripts/Publish-Windows.ps1')
if ($Version) { $publishArguments += @('-Version', $Version) }
$buildExit = Invoke-Docker (@(
        'run', '--rm',
        '-v', $mount,
        # NuGet packages are kept in a Docker volume so later updates don't download them again.
        '-v', 'entitytracker-nuget:/root/.nuget/packages',
        '-w', '/src',
        '-e', 'DOTNET_CLI_TELEMETRY_OPTOUT=1',
        '-e', 'DOTNET_NOLOGO=1',
        $image, 'pwsh') + $publishArguments)
if ($buildExit -ne 0) {
    throw "The app build in Docker failed with exit code $buildExit."
}
