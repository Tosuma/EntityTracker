[CmdletBinding()]
param()

. (Join-Path $PSScriptRoot 'Install-Common.ps1')
$testParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
$testRoot = Join-Path $testParent ('EntityTracker-install-swap-' + [guid]::NewGuid().ToString('N'))
$current = Join-Path $testRoot 'EntityTracker'
$staged = Join-Path $testRoot 'EntityTracker.new'
$previous = Join-Path $testRoot 'EntityTracker.old'

function Assert-Content {
    param([string]$Path, [string]$Expected)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf) -or
        (Get-Content -LiteralPath $Path -Raw).Trim() -ne $Expected) {
        throw "Expected $Path to contain '$Expected'."
    }
}

try {
    New-Item -ItemType Directory -Path $current, $staged -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $current 'version.txt') -Value 'old'
    Set-Content -LiteralPath (Join-Path $staged 'version.txt') -Value 'new'

    $lock = [IO.File]::Open((Join-Path $current 'version.txt'),
        [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
    try {
        try {
            Invoke-InstallSwap $testRoot $current $staged $previous {}
            throw 'The locked install unexpectedly moved.'
        }
        catch {
            if ($_.Exception.Message -notlike '*install folder is in use*') { throw }
        }
    }
    finally { $lock.Dispose() }
    Assert-Content (Join-Path $current 'version.txt') 'old'
    Assert-Content (Join-Path $staged 'version.txt') 'new'
    if (Test-Path -LiteralPath $previous) { throw 'The failed swap left a previous-install folder.' }

    try {
        Invoke-InstallSwap $testRoot $current $staged $previous { throw 'Activation failed' }
        throw 'The failing activation unexpectedly succeeded.'
    }
    catch {
        if ($_.Exception.Message -notlike '*Activation failed*') { throw }
    }
    Assert-Content (Join-Path $current 'version.txt') 'old'
    if (Test-Path -LiteralPath $previous) { throw 'The previous install was not restored.' }

    New-Item -ItemType Directory -Path $staged | Out-Null
    Set-Content -LiteralPath (Join-Path $staged 'version.txt') -Value 'new'
    Invoke-InstallSwap $testRoot $current $staged $previous {}
    Assert-Content (Join-Path $current 'version.txt') 'new'
    if (Test-Path -LiteralPath $previous) { throw 'The successful swap kept the previous install.' }

    Write-Host 'Installer swap tests passed.'
}
finally {
    $resolvedRoot = [IO.Path]::GetFullPath($testRoot)
    if ($resolvedRoot.StartsWith($testParent + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedRoot)) {
        Remove-Item -LiteralPath $resolvedRoot -Recurse -Force
    }
}
