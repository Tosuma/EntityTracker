<#
.SYNOPSIS
Replaces EntityTracker progress data with deterministic synthetic history.

.DESCRIPTION
Close EntityTracker before running this script. The selected database is modified in place:
active statuses and all progress history are replaced, without a backup. Entity identities,
names, notes, dependencies, overrides, provenance, and archive state are preserved.

.PARAMETER ConfirmReset
Required acknowledgement of the destructive progress reset.

.PARAMETER Days
Inclusive history length. Defaults to 90 days and must be at least 7.

.PARAMETER Seed
Random seed used to make the generated timeline repeatable. Defaults to 12345.

.PARAMETER DatabasePath
Database to modify. Defaults to the WPF project's Debug database.

.EXAMPLE
.\scripts\Seed-ProgressDemo.ps1 -ConfirmReset

.EXAMPLE
.\scripts\Seed-ProgressDemo.ps1 -ConfirmReset -Days 120 -Seed 42
#>
[CmdletBinding()]
param(
    [switch]$ConfirmReset,
    [ValidateRange(7, [int]::MaxValue)]
    [int]$Days = 90,
    [int]$Seed = 12345,
    [string]$DatabasePath
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot

if (-not $ConfirmReset) {
    throw 'Pass -ConfirmReset to acknowledge that existing progress statuses and history will be replaced without a backup.'
}

if (Get-Process -Name 'EntityTracker.Wpf' -ErrorAction SilentlyContinue) {
    throw 'Close EntityTracker before seeding its database.'
}

if ([string]::IsNullOrWhiteSpace($DatabasePath)) {
    $DatabasePath = Join-Path $repositoryRoot 'src\EntityTracker.Wpf\bin\Debug\net10.0-windows\entity-tracker.db'
}

if (-not (Test-Path -LiteralPath $DatabasePath -PathType Leaf)) {
    throw "EntityTracker database not found at '$DatabasePath'. Run the application and import data first, or pass -DatabasePath."
}

$resolvedDatabasePath = (Resolve-Path -LiteralPath $DatabasePath).Path
$toolProject = Join-Path $repositoryRoot 'tools\EntityTracker.DemoData\EntityTracker.DemoData.csproj'

Write-Warning 'Existing progress statuses and progress history will be replaced. No backup will be created.'
& dotnet run --project $toolProject --no-restore -- `
    --database $resolvedDatabasePath `
    --days $Days `
    --seed $Seed `
    --confirm-reset

if ($LASTEXITCODE -ne 0) {
    throw "The demo-data utility exited with code $LASTEXITCODE."
}

Write-Host 'Synthetic progress is ready. Start EntityTracker and open the Progress page.'
