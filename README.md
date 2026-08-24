# EntityTracker

EntityTracker is a Windows desktop application for importing PostgreSQL table dependencies, preserving development progress against stable entities, and showing dependency-safe work order and progress reports.

## Run from source

The repository uses the .NET SDK version in `global.json`.

```powershell
dotnet run --project src/EntityTracker.Wpf/EntityTracker.Wpf.csproj
```

## Create a Windows package

From PowerShell at the repository root:

```powershell
.\scripts\Publish-Windows.ps1
```

The script creates a self-contained Windows x64 ZIP at `artifacts\EntityTracker-win-x64.zip`. A target computer does not need a separate .NET installation. Extract the ZIP and double-click `EntityTracker.Wpf.exe`; normal use requires no command line.

## Import a PostgreSQL schema

1. Open **Schema Synchronization**.
2. If you already have a compatible CSV, choose the import type and click **Choose CSV and Review** immediately.
3. If you need the PostgreSQL extraction query, click **Get SQL Query**. Copy and run it in PostgreSQL, then export its result as UTF-8 CSV with a semicolon delimiter and headers.
4. Review and apply the proposed changes. A Complete import asks for confirmation before it archives entities absent from the CSV.

The query and CSV contract are independent: opening or copying the SQL is never required to import an existing CSV. The exact versioned contract is documented in [schema-csv-contract-v1.md](docs/importing/schema-csv-contract-v1.md).

## Connections and local data

The **Connections** page can save a friendly name and HTTPS SharePoint site URL for future approved
integration. This build does not connect, authenticate, synchronize, or switch providers after that
setup is saved. SQLite remains active. No credentials or tokens are stored in the settings file.

EntityTracker stores its SQLite working database at:

```text
%LOCALAPPDATA%\EntityTracker\entity-tracker.db
```

If this destination does not exist and an older `entity-tracker.db` is beside the executable,
EntityTracker copies that legacy database into the local-data folder once. Later launches always
use the local-data copy.

EntityTracker also keeps optional `settings.json`, daily logs, and automatic daily/pre-migration
database backups under `%LOCALAPPDATA%\EntityTracker`. The newest 14 logs and 14 backups are
retained. See [local recovery, backups, and logs](docs/operations/RECOVERY.md) before replacing or
restoring application data. The future collaborative storage rules and replacement seams are in
[the collaborative storage contract](docs/architecture/COLLABORATIVE_STORAGE.md).
