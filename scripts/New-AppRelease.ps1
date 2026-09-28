[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Major', 'Minor', 'Patch')]
    [string]$Bump,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$Message,

    [switch]$MainCiPassed
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot

function Invoke-Git {
    param([string[]]$Arguments)
    $result = & git -C $repositoryRoot @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
    return $result
}

if (-not $MainCiPassed) {
    throw 'Check that the main commit passed both GitHub Actions jobs, then pass -MainCiPassed.'
}
if ([string]::IsNullOrWhiteSpace($Message)) {
    throw 'The release message must contain text.'
}

$actualRoot = Invoke-Git -Arguments @('rev-parse', '--show-toplevel')
if ([IO.Path]::GetFullPath($actualRoot).TrimEnd('\', '/') -ne
    [IO.Path]::GetFullPath($repositoryRoot).TrimEnd('\', '/')) {
    throw 'The release script must be inside the root of its source checkout.'
}

$branch = Invoke-Git -Arguments @('symbolic-ref', '--quiet', '--short', 'HEAD')
if ($branch -ne 'main') {
    throw "Switch to main before releasing (current branch: $branch)."
}
if (Invoke-Git -Arguments @('status', '--porcelain', '--untracked-files=normal')) {
    throw 'The working tree is not clean. Commit or remove local changes before releasing.'
}

$head = Invoke-Git -Arguments @('rev-parse', 'HEAD')
$remoteRefs = @(Invoke-Git -Arguments @('ls-remote', '--refs', 'origin', 'refs/heads/main', 'refs/tags/app-v*'))
$mainRef = @($remoteRefs | Where-Object { $_ -match '^[0-9a-fA-F]{40}\s+refs/heads/main$' })
if ($mainRef.Count -ne 1) {
    throw 'Could not identify origin/main. Check the origin remote.'
}
$remoteHead = ($mainRef[0] -split '\s+')[0]
if ($head -ne $remoteHead) {
    throw 'Local main does not match origin/main. Run git pull --ff-only and check the main CI run again.'
}

$versions = foreach ($line in $remoteRefs) {
    if ($line -match '^[0-9a-fA-F]{40}\s+refs/tags/app-v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
        $parsed = $null
        if ([version]::TryParse("$($Matches[1]).$($Matches[2]).$($Matches[3])", [ref]$parsed)) {
            $parsed
        }
    }
}
$latest = $versions | Sort-Object -Descending | Select-Object -First 1
if ($null -eq $latest) {
    $next = [version]'1.0.0'
} else {
    $component = switch ($Bump) {
        'Major' { $latest.Major }
        'Minor' { $latest.Minor }
        'Patch' { $latest.Build }
    }
    if ($component -eq [int]::MaxValue) {
        throw "Cannot increment the $Bump version component any further."
    }
    switch ($Bump) {
        'Major' { $next = [version]::new(($latest.Major + 1), 0, 0) }
        'Minor' { $next = [version]::new($latest.Major, ($latest.Minor + 1), 0) }
        'Patch' { $next = [version]::new($latest.Major, $latest.Minor, ($latest.Build + 1)) }
    }
}
$tag = "app-v$next"

# Do not silently replace a local tag, even when it has not yet been pushed.
& git -C $repositoryRoot show-ref --verify --quiet "refs/tags/$tag"
if ($LASTEXITCODE -eq 0) {
    throw "A local $tag tag already exists. Inspect it before retrying the push."
}
if ($LASTEXITCODE -ne 1) {
    throw 'Could not check whether the release tag already exists locally.'
}

Write-Host "Creating $tag on $head"
Write-Host "Release message: $Message"
Invoke-Git -Arguments @('tag', '-a', $tag, '-m', $Message, $head) | Out-Null

try {
    Invoke-Git -Arguments @('push', 'origin', "refs/tags/${tag}:refs/tags/${tag}") | Out-Null
} catch {
    throw "Could not push $tag. The local tag remains. Inspect the failure, then retry with: git push origin refs/tags/${tag}:refs/tags/${tag}`n$($_.Exception.Message)"
}

Write-Host "Published $tag. Check its GitHub Actions run before announcing the release."
