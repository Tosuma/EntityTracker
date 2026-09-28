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
