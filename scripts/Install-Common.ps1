Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-RequiredCommand {
    param([string]$File, [string[]]$Arguments)
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$File failed with exit code $LASTEXITCODE."
    }
}

function Get-StableReleaseTag {
    param([string]$SourcePath)
    $refs = & git -C $SourcePath ls-remote --refs --tags origin 'app-v*'
    if ($LASTEXITCODE -ne 0) { throw 'Could not check approved app release tags.' }
    $versions = foreach ($line in $refs) {
        if ($line -match 'refs/tags/(app-v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*))$') {
            [pscustomobject]@{
                Tag = $Matches[1]
                Version = [version]("$($Matches[2]).$($Matches[3]).$($Matches[4])")
            }
        }
    }
    $latest = $versions | Sort-Object Version -Descending | Select-Object -First 1
    if ($null -eq $latest) { throw 'No app-vX.Y.Z release tag exists on origin.' }
    return $latest.Tag
}

function Assert-ChildPath {
    param([string]$Parent, [string]$Child)
    $parentFull = [IO.Path]::GetFullPath($Parent).TrimEnd('\', '/')
    $childFull = [IO.Path]::GetFullPath($Child)
    if (-not $childFull.StartsWith($parentFull + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to change a path outside $parentFull."
    }
}
