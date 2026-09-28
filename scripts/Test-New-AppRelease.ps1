[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$releaseScript = Join-Path $PSScriptRoot 'New-AppRelease.ps1'
$tempParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
$testRoot = Join-Path $tempParent ('EntityTracker-release-tests-' + [guid]::NewGuid().ToString('N'))

function Invoke-FixtureGit {
    param([string]$Repository, [Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)
    $output = & git -C $Repository @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Fixture git $($Arguments -join ' ') failed."
    }
    return $output
}

function New-Fixture {
    param([string]$Name)
    $folder = Join-Path $testRoot $Name
    $remote = Join-Path $folder 'remote.git'
    $checkout = Join-Path $folder 'checkout'
    New-Item -ItemType Directory -Path $folder | Out-Null
    & git init --bare $remote | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not initialize fixture remote.' }
    & git init -b main $checkout | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not initialize fixture checkout.' }
    Invoke-FixtureGit $checkout @('config', 'user.name', 'Release Test') | Out-Null
    Invoke-FixtureGit $checkout @('config', 'user.email', 'release-test@example.invalid') | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $checkout 'scripts') | Out-Null
    Copy-Item -LiteralPath $releaseScript -Destination (Join-Path $checkout 'scripts\New-AppRelease.ps1')
    Set-Content -LiteralPath (Join-Path $checkout 'README.md') -Value 'Fixture'
    Invoke-FixtureGit $checkout @('add', '.') | Out-Null
    Invoke-FixtureGit $checkout @('commit', '-m', 'Initial fixture') | Out-Null
    Invoke-FixtureGit $checkout @('remote', 'add', 'origin', $remote) | Out-Null
    Invoke-FixtureGit $checkout @('push', '-u', 'origin', 'main') | Out-Null
    return $checkout
}

function Assert-Equal {
    param($Expected, $Actual, [string]$Description)
    if ($Expected -ne $Actual) {
        throw "$Description`: expected '$Expected', got '$Actual'."
    }
}

function Assert-Fails {
    param([scriptblock]$Action, [string]$ExpectedText)
    try {
        & $Action | Out-Null
    } catch {
        if ($_.Exception.Message -notlike "*$ExpectedText*") {
            throw "Expected failure containing '$ExpectedText', got '$($_.Exception.Message)'."
        }
        return
    }
    throw "Expected failure containing '$ExpectedText', but the command succeeded."
}

function Invoke-Release {
    param([string]$Checkout, [string]$Bump, [string]$Message)
    & (Join-Path $Checkout 'scripts\New-AppRelease.ps1') -Bump $Bump -Message $Message -MainCiPassed
}

function Assert-Tag {
    param([string]$Checkout, [string]$Tag, [string]$Message)
    $localCommit = Invoke-FixtureGit $Checkout @('rev-parse', 'HEAD')
    $tagCommit = Invoke-FixtureGit $Checkout @('rev-parse', "refs/tags/$Tag^{}")
    Assert-Equal $localCommit $tagCommit "$Tag target"
    $tagType = Invoke-FixtureGit $Checkout @('cat-file', '-t', "refs/tags/$Tag")
    Assert-Equal 'tag' $tagType "$Tag annotation type"
    $annotation = (Invoke-FixtureGit $Checkout @('for-each-ref', '--format=%(contents)', "refs/tags/$Tag") | Out-String).Trim()
    Assert-Equal $Message $annotation "$Tag message"
    $remoteRef = Invoke-FixtureGit $Checkout @('ls-remote', '--refs', '--tags', 'origin', "refs/tags/$Tag")
    if (-not $remoteRef) { throw "$Tag was not pushed to origin." }
}

try {
    New-Item -ItemType Directory -Path $testRoot | Out-Null
    $checkout = New-Fixture 'sequence'
    $script = Join-Path $checkout 'scripts\New-AppRelease.ps1'

    Assert-Fails { & $script -Bump Major -Message 'No CI flag' } 'MainCiPassed'
    Invoke-Release $checkout Patch 'First release'
    Assert-Tag $checkout 'app-v1.0.0' 'First release'
    Invoke-Release $checkout Patch 'Patch notes'
    Assert-Tag $checkout 'app-v1.0.1' 'Patch notes'
    Invoke-Release $checkout Minor 'Minor notes'
    Assert-Tag $checkout 'app-v1.1.0' 'Minor notes'
    Invoke-Release $checkout Major 'Major notes'
    Assert-Tag $checkout 'app-v2.0.0' 'Major notes'

    Invoke-FixtureGit $checkout @('tag', '-a', 'app-v2.0.1', '-m', 'Local only') | Out-Null
    Assert-Fails { Invoke-Release $checkout Patch 'Duplicate' } 'already exists'
    Invoke-FixtureGit $checkout @('tag', '-d', 'app-v2.0.1') | Out-Null

    Set-Content -LiteralPath (Join-Path $checkout 'untracked.txt') -Value 'Not committed'
    Assert-Fails { Invoke-Release $checkout Patch 'Dirty' } 'not clean'
    Remove-Item -LiteralPath (Join-Path $checkout 'untracked.txt')

    Invoke-FixtureGit $checkout @('switch', '-c', 'feature') | Out-Null
    Assert-Fails { Invoke-Release $checkout Patch 'Wrong branch' } 'Switch to main'
    Invoke-FixtureGit $checkout @('switch', 'main') | Out-Null

    Set-Content -LiteralPath (Join-Path $checkout 'README.md') -Value 'Local commit'
    Invoke-FixtureGit $checkout @('add', 'README.md') | Out-Null
    Invoke-FixtureGit $checkout @('commit', '-m', 'Local only') | Out-Null
    Assert-Fails { Invoke-Release $checkout Patch 'Stale main' } 'does not match origin/main'

    foreach ($bump in @('Major', 'Minor')) {
        $firstCheckout = New-Fixture "first-$bump"
        Invoke-Release $firstCheckout $bump "First $bump"
        Assert-Tag $firstCheckout 'app-v1.0.0' "First $bump"
    }

    $rejectedCheckout = New-Fixture 'rejected-push'
    $remoteHook = Join-Path (Split-Path -Parent $rejectedCheckout) 'remote.git\hooks\pre-receive'
    [IO.File]::WriteAllText($remoteHook, "#!/bin/sh`nexit 1`n")
    Assert-Fails { Invoke-Release $rejectedCheckout Patch 'Rejected release' } 'local tag remains'
    $remainingTag = Invoke-FixtureGit $rejectedCheckout @('tag', '--list', 'app-v1.0.0')
    Assert-Equal 'app-v1.0.0' $remainingTag 'local tag after failed push'
    $remoteTag = Invoke-FixtureGit $rejectedCheckout @('ls-remote', '--refs', '--tags', 'origin', 'refs/tags/app-v1.0.0')
    Assert-Equal $null $remoteTag 'remote tag after failed push'

    Write-Host 'Release script tests passed.'
} finally {
    $resolvedRoot = [IO.Path]::GetFullPath($testRoot)
    if ($resolvedRoot.StartsWith($tempParent + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedRoot)) {
        Remove-Item -LiteralPath $resolvedRoot -Recurse -Force
    }
}
