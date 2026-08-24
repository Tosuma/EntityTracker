# Development, Build, and Run Guide

This guide contains the operational information for building, testing, running, publishing, and
using EntityTracker from source.

## Prerequisites

- Windows 10 or Windows 11
- PowerShell
- Git
- The .NET SDK version selected by [`global.json`](../global.json), currently `10.0.400`

Visual Studio is optional. All required workflows are available from PowerShell and the .NET CLI.

## Clone and restore

```powershell
git clone https://github.com/Tosuma/Entity-Dependency-Manager.git
cd Entity-Dependency-Manager
dotnet restore EntityTracker.slnx
```

## Build the complete solution

```powershell
dotnet build EntityTracker.slnx --no-restore
```

For the same configuration used by packaging and the planned CI workflow:

```powershell
dotnet build EntityTracker.slnx --configuration Release --no-restore
```

## Run all tests

After a successful build:

```powershell
dotnet test EntityTracker.slnx --no-build --no-restore
```

Use the matching configuration when testing a Release build:

```powershell
dotnet test EntityTracker.slnx --configuration Release --no-build --no-restore
```

## Run the WPF application

From the repository root:

```powershell
dotnet run --project src/EntityTracker.Wpf/EntityTracker.Wpf.csproj
```

Passing `--project` makes the WPF startup project explicit regardless of the current solution or
IDE configuration.

## Create a self-contained Windows package

From PowerShell at the repository root:

```powershell
.\scripts\Publish-Windows.ps1
```

The script publishes a Release build by default and creates:

```text
artifacts\EntityTracker-win-x64.zip
```

The ZIP is self-contained for Windows x64, so a target computer does not need a separate .NET
installation. Extract the ZIP and start `EntityTracker.Wpf.exe`; normal use requires no command
line.

To publish a Debug package explicitly:

```powershell
.\scripts\Publish-Windows.ps1 -Configuration Debug
```

The script replaces its existing publish directory and ZIP under `artifacts`. That directory is
generated output and is ignored by Git.

## Import a PostgreSQL schema

1. Open **Schema Synchronization**.
2. Choose the import type:
   - **Complete** is the default. Entities absent from the CSV may be archived after review and
     confirmation.
   - **Partial** leaves persisted entities absent from the CSV unchanged.
3. If you already have a compatible CSV, select **Choose CSV and Review**.
4. If you need the extraction query, select **Get SQL Query**, copy it, and run it in PostgreSQL.
5. Export the query result as UTF-8 CSV with a semicolon (`;`) delimiter and a header row.
6. Review all actionable differences and apply the synchronization when satisfied.

Nothing is saved merely by selecting a CSV. Canceling review changes no persisted state.

The query helper and CSV import are independent: opening or copying the SQL is never required to
import an existing compatible file. The exact versioned input format is documented in the
[schema CSV contract](importing/schema-csv-contract-v1.md).

## Connections and current storage behavior

The **Connections** page can save a friendly display name and an HTTPS SharePoint site URL for
future approved integration. The current application does not authenticate, validate remote
access, connect, synchronize, or switch providers after saving that setup. SQLite remains active.

The settings file stores no credentials or tokens. Live SharePoint behavior belongs to
[Milestone 13](milestones/13_sharepoint_integration.md).

## Local application data

EntityTracker stores runtime data for the current Windows user under:

```text
%LOCALAPPDATA%\EntityTracker\
```

The active files and directories are:

```text
entity-tracker.db    SQLite database
settings.json        optional non-secret connection setup
backups\             automatic SQLite backups
logs\                daily application logs
```

Running from source and running a published ZIP use the same Local Application Data database for
the same Windows user. They therefore show the same tracked data unless one process is run under a
different user profile or its data path is deliberately changed in code.

If the Local Application Data database does not exist and an older `entity-tracker.db` is beside
the executable, EntityTracker copies that legacy database into the local-data folder once. Later
launches always use the local-data copy.

EntityTracker retains the newest 14 daily logs and 14 automatic daily/pre-migration backups. Read
the [recovery guide](operations/RECOVERY.md) before replacing, restoring, or resetting application
data.

## Repository structure

```text
src/       production projects
tests/     automated test projects
tools/     local development utilities
scripts/   publishing and development scripts
docs/      architecture, operations, contracts, and milestones
images/    README screenshots
```

See the [architecture rules](architecture/ARCHITECTURE.md) before changing project references or
moving business behavior between layers.
