Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-ChildPath {
    param([string]$Parent, [string]$Child)
    $parentFull = [IO.Path]::GetFullPath($Parent).TrimEnd('\', '/')
    $childFull = [IO.Path]::GetFullPath($Child)
    if (-not $childFull.StartsWith($parentFull + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to change a path outside $parentFull."
    }
}

function Invoke-InstallSwap {
    param(
        [string]$InstallParent,
        [string]$CurrentPath,
        [string]$StagedPath,
        [string]$PreviousPath,
        [scriptblock]$Activate
    )
    foreach ($candidate in @($CurrentPath, $StagedPath, $PreviousPath)) {
        Assert-ChildPath $InstallParent $candidate
    }

    $movedOld = $false
    $installedNew = $false
    try {
        if (Test-Path -LiteralPath $CurrentPath) {
            try { [IO.Directory]::Move($CurrentPath, $PreviousPath) }
            catch {
                throw "Could not replace EntityTracker because its install folder is in use. Close the app and any updater, then retry. $($_.Exception.Message)"
            }
            $movedOld = $true
        }
        [IO.Directory]::Move($StagedPath, $CurrentPath)
        $installedNew = $true
        & $Activate $CurrentPath
    }
    catch {
        $installError = $_
        if ($installedNew -and (Test-Path -LiteralPath $CurrentPath)) {
            try { Remove-Item -LiteralPath $CurrentPath -Recurse -Force }
            catch { Write-Warning "Could not remove the incomplete new install: $CurrentPath" }
        }
        if ($movedOld) {
            try { [IO.Directory]::Move($PreviousPath, $CurrentPath) }
            catch { Write-Warning "Could not restore the previous install: $PreviousPath" }
        }
        throw $installError
    }

    if ($movedOld) {
        try { Remove-Item -LiteralPath $PreviousPath -Recurse -Force }
        catch { Write-Warning "The previous install could not be removed: $PreviousPath" }
    }
}

# ---------------------------------------------------------------------------------------------
# Docker. Installing and updating run in .NET SDK containers from mcr.microsoft.com, so Docker is
# the only requirement: one fetches and checks the release, and the SDK image the release pins
# builds it and saves the result to the install folder. Only what a Linux container cannot do happens on Windows: waiting for the app to
# close, swapping the install folders, the Start Menu shortcut and starting the app.
# ---------------------------------------------------------------------------------------------

# The image that fetches the release; the SDK images include git. The release's own SDK version
# is only known after the fetch, so this is pinned here. Test-DockerInstall.ps1 keeps it equal to
# global.json, so it is the image the build then uses and nothing extra is downloaded.
$script:FetchSdkVersion = '10.0.400'
$script:FetchImage = "mcr.microsoft.com/dotnet/sdk:$script:FetchSdkVersion"
$script:NuGetVolume = 'entitytracker-nuget'
$script:DockerHelp = 'EntityTracker installs with Docker. Start Docker Desktop or Rancher Desktop (with the ' +
    'Docker engine and Linux containers), then run the install or update again.'

# Docker writes progress to stderr. Windows PowerShell 5.1 turns that into errors that would stop the
# script, so docker runs with errors allowed and only its exit code decides. Everything is shown.
function Invoke-Docker {
    param([string[]]$Arguments)
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & docker @Arguments 2>&1 | ForEach-Object { Write-Host "  $_" } }
    finally { $ErrorActionPreference = $previous }
    return $LASTEXITCODE
}

# Runs docker and returns what it writes to stdout; what it writes to stderr is shown unless -Quiet.
function Get-DockerOutput {
    param([string[]]$Arguments, [switch]$Quiet)
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $lines = [System.Collections.Generic.List[string]]::new()
    try {
        & docker @Arguments 2>&1 | ForEach-Object {
            if ($_ -is [System.Management.Automation.ErrorRecord]) { if (-not $Quiet) { Write-Host "  $_" } }
            else { $lines.Add("$_") }
        }
    }
    finally { $ErrorActionPreference = $previous }
    return [pscustomobject]@{ Lines = $lines.ToArray(); Output = ($lines -join "`n").Trim(); ExitCode = $LASTEXITCODE }
}

function Assert-DockerReady {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        throw "Docker was not found. $script:DockerHelp"
    }
    $info = Get-DockerOutput @('info', '--format', '{{.OSType}}') -Quiet
    if ($info.ExitCode -ne 0) { throw "Docker is not running. $script:DockerHelp" }
    if ($info.Output -ne 'linux') {
        throw 'Docker is set to Windows containers. Switch it to Linux containers, then run the install or update again.'
    }
}

function Assert-DockerImage {
    param([string]$Image, [string]$Description)
    if ((Get-DockerOutput @('image', 'inspect', '--format', '{{.Id}}', $Image) -Quiet).ExitCode -eq 0) { return }
    Write-Host "  Downloading $Description. The first time takes a while; later updates reuse it."
    if ((Invoke-Docker @('pull', $Image)) -ne 0) {
        throw "Could not download $Image. Check the internet connection and that Docker is running."
    }
}

# Runs a shell script in a container. The script and every value reach the container as environment
# variables, the script base64-encoded, because Windows PowerShell 5.1 breaks native arguments that
# contain quotes. ET_STEP names the step for tests. -Capture returns stdout instead of showing it.
function Invoke-ContainerScript {
    param(
        [string]$Image,
        [string]$Step,
        [string]$Script,
        [string[]]$Volumes = @(),
        [hashtable]$Environment = @{},
        [switch]$Capture
    )
    $encoded = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes(($Script -replace "`r", '')))
    $arguments = @('run', '--rm')
    foreach ($volume in $Volumes) { $arguments += @('-v', $volume) }
    $arguments += @('-e', "ET_STEP=$Step")
    foreach ($name in ($Environment.Keys | Sort-Object)) { $arguments += @('-e', "$name=$($Environment[$name])") }
    $arguments += @('-e', "ET_SCRIPT=$encoded", '--entrypoint', 'sh', $Image,
        '-c', 'printf %s $ET_SCRIPT | base64 -d > /tmp/et-step.sh && sh -eu /tmp/et-step.sh')
    if ($Capture) { return Get-DockerOutput $arguments }
    return [pscustomobject]@{ Lines = @(); Output = ''; ExitCode = (Invoke-Docker $arguments) }
}

# The URL of the source clone's origin, read from its Git configuration (no Git needed on Windows).
function Get-OriginUrl {
    param([string]$SourcePath)
    $notCheckout = 'SourcePath must be the root of an existing app source checkout.'
    $gitPath = Join-Path $SourcePath '.git'
    if (Test-Path -LiteralPath $gitPath -PathType Leaf) {
        # A worktree: .git names its Git folder, whose commondir names the repository's.
        $line = Get-Content -LiteralPath $gitPath -TotalCount 1
        if ($line -notmatch '^gitdir:\s*(.+)$') { throw $notCheckout }
        $gitPath = [IO.Path]::GetFullPath((Join-Path $SourcePath $Matches[1].Trim()))
        $common = Join-Path $gitPath 'commondir'
        if (Test-Path -LiteralPath $common -PathType Leaf) {
            $gitPath = [IO.Path]::GetFullPath((Join-Path $gitPath (Get-Content -LiteralPath $common -TotalCount 1).Trim()))
        }
    }
    $config = Join-Path $gitPath 'config'
    if (-not (Test-Path -LiteralPath $config -PathType Leaf)) { throw $notCheckout }
    $inOrigin = $false
    foreach ($line in Get-Content -LiteralPath $config) {
        if ($line -match '^\s*\[(.+)\]\s*$') { $inOrigin = $Matches[1].Trim() -eq 'remote "origin"'; continue }
        if ($inOrigin -and $line -match '^\s*url\s*=\s*(.+?)\s*$') { return $Matches[1] }
    }
    throw 'The source checkout has no origin remote.'
}

# The containers read the repository without credentials, so an SSH origin is read over HTTPS and
# any user name or token in the URL is left out.
function ConvertTo-AnonymousUrl {
    param([string]$Url)
    if ($Url -match '^[^@/:\s]+@([^:/\s]+):(.+)$') { return "https://$($Matches[1])/$($Matches[2])" }
    if ($Url -match '^ssh://(?:[^@/]+@)?([^:/]+)(?::\d+)?/(.+)$') { return "https://$($Matches[1])/$($Matches[2])" }
    if ($Url -match '^(https?://)[^@/]+@(.+)$') { return "$($Matches[1])$($Matches[2])" }
    return $Url
}

# The newest app-vX.Y.Z tag in `git ls-remote --refs --tags` output, or -Tag, with its hash.
function Select-ReleaseTag {
    param([string[]]$Refs, [string]$Tag)
    $releases = @(foreach ($line in $Refs) {
            if ($line -match '^([0-9a-fA-F]{40})\s+refs/tags/(app-v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*))$') {
                [pscustomobject]@{
                    Hash = $Matches[1].ToLowerInvariant(); Tag = $Matches[2]
                    Version = [version]("$($Matches[3]).$($Matches[4]).$($Matches[5])")
                }
            }
        })
    $selected = if ($Tag) { $releases | Where-Object Tag -eq $Tag | Select-Object -First 1 }
        else { $releases | Sort-Object Version -Descending | Select-Object -First 1 }
    if ($null -eq $selected) {
        if ($Tag) { throw "The approved release tag $Tag was not found on origin." }
        throw 'No app-vX.Y.Z release tag exists on origin.'
    }
    return $selected
}

# The exact .NET SDK version a release's global.json pins; it names the build image.
function Get-SdkVersion {
    param([string]$GlobalJson)
    $version = $null
    try { $version = ($GlobalJson | ConvertFrom-Json).sdk.version } catch { }
    if ("$version" -notmatch '^\d+\.\d+\.\d+$') {
        throw "The release's global.json does not pin an exact .NET SDK version."
    }
    return "$version"
}

# The release tags on origin, as `git ls-remote --refs --tags` lists them.
function Get-ReleaseRefs {
    param([string]$OriginUrl)
    $refs = Invoke-ContainerScript -Image $script:FetchImage -Step 'tags' -Capture `
        -Environment @{ ET_ORIGIN = $OriginUrl } `
        -Script "git ls-remote --refs --tags `"`$ET_ORIGIN`" 'app-v*'"
    if ($refs.ExitCode -ne 0) {
        throw "Could not read the release tags from $OriginUrl. The install reads the repository without " +
            'credentials; check the internet connection and that the repository allows anonymous read access.'
    }
    return $refs.Lines
}

# Fetches the release (from Select-ReleaseTag) into /work/source of the work volume and returns the
# .NET SDK version it pins. The fetched tag must be the one origin listed; like the earlier
# `git archive`, the source is kept without its .git folder.
function Invoke-ReleaseFetch {
    param([string]$OriginUrl, $Release, [string]$Volume)
    $fetched = Invoke-ContainerScript -Image $script:FetchImage -Step 'fetch' -Capture `
        -Volumes @("${Volume}:/work") `
        -Environment @{ ET_ORIGIN = $OriginUrl; ET_TAG = $Release.Tag; ET_HASH = $Release.Hash } `
        -Script @'
mkdir -p /work/source
cd /work/source
git init -q
git remote add origin "$ET_ORIGIN"
git fetch -q --depth 1 --no-tags origin "+refs/tags/$ET_TAG:refs/tags/$ET_TAG"
if [ "$(git rev-parse "refs/tags/$ET_TAG")" != "$ET_HASH" ]; then exit 42; fi
git -c advice.detachedHead=false checkout -q "refs/tags/$ET_TAG"
rm -rf .git
cat global.json
'@
    if ($fetched.ExitCode -eq 42) { throw "The fetched $($Release.Tag) tag differs from origin. Refusing to install." }
    if ($fetched.ExitCode -ne 0) { throw "Could not fetch $($Release.Tag) from $OriginUrl (exit code $($fetched.ExitCode))." }
    return Get-SdkVersion $fetched.Output
}

# Copies the build from the work volume to <InstallParent>/<FolderName> with its app-install.json,
# in the SDK image the build used. A write probe first tells a missing drive share apart.
function Save-ContainerBuild {
    param([string]$SdkVersion, [string]$Volume, [string]$InstallParent, [string]$FolderName, [string]$Manifest)
    New-Item -ItemType Directory -Path $InstallParent -Force | Out-Null
    $saved = Invoke-ContainerScript -Image "mcr.microsoft.com/dotnet/sdk:$SdkVersion" -Step 'save' `
        -Volumes @("${Volume}:/work", "${InstallParent}:/programs") `
        -Environment @{
            ET_NEW = $FolderName
            ET_MANIFEST = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($Manifest))
        } `
        -Script @'
probe="/programs/.$ET_NEW.write-check"
if ! touch "$probe" 2>/dev/null; then exit 42; fi
rm -f "$probe"
publish=/work/source/artifacts/publish/win-x64
if [ ! -f "$publish/EntityTracker.Wpf.exe" ]; then exit 43; fi
cp -R "$publish" "/programs/$ET_NEW"
printf %s "$ET_MANIFEST" | base64 -d > "/programs/$ET_NEW/app-install.json"
'@
    if ($saved.ExitCode -eq 42) {
        throw "Docker could not write to $InstallParent. Allow Docker Desktop or Rancher Desktop to share this drive, then run the install or update again."
    }
    if ($saved.ExitCode -eq 43) { throw 'The app build did not produce EntityTracker.Wpf.exe.' }
    if ($saved.ExitCode -ne 0) { throw "Could not save the app to $InstallParent (exit code $($saved.ExitCode))." }
}

# Builds the app in the .NET SDK container from the source folder that -SourceMount mounts, with
# NuGet packages kept in a volume. The installer mounts its work volume; CI mounts its checkout.
function Invoke-ContainerBuild {
    param([string]$SdkVersion, [string]$SourceMount, [string]$WorkingDirectory, [string]$Version)
    $image = "mcr.microsoft.com/dotnet/sdk:$SdkVersion"
    Assert-DockerImage $image ".NET SDK $SdkVersion (about 1 GB)"
    $arguments = @('run', '--rm', '-v', $SourceMount, '-v', "${script:NuGetVolume}:/root/.nuget/packages",
        '-w', $WorkingDirectory, '-e', 'ET_STEP=build', '-e', 'DOTNET_CLI_TELEMETRY_OPTOUT=1', '-e', 'DOTNET_NOLOGO=1',
        $image, 'pwsh', '-NoProfile', '-NonInteractive', '-File', 'scripts/Publish-Windows.ps1')
    if ($Version) { $arguments += @('-Version', $Version) }
    $exit = Invoke-Docker $arguments
    if ($exit -ne 0) { throw "The app build in Docker failed with exit code $exit." }
}
