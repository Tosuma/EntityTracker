# Getting Started

This guide walks you through cloning, building, and running EntityTracker for the first time.

## Prerequisites

- Windows 10 or 11
- [.NET SDK 10.0.400](https://dotnet.microsoft.com/download/dotnet/10.0) (pinned by `global.json`)
- Git
- Network access to `nuget.org` (for the initial package restore)

Verify your SDK version:

```powershell
dotnet --version
# Should output: 10.0.4xx
```

## Clone and build

```powershell
git clone https://github.com/Tosuma/Entity-Dependency-Manager.git
cd Entity-Dependency-Manager
dotnet restore EntityTracker.slnx
dotnet build EntityTracker.slnx --no-restore
```

If restore fails with `NU1100` errors (usually a transient network issue), clear the NuGet
HTTP cache and retry:

```powershell
dotnet nuget locals all --clear
dotnet restore EntityTracker.slnx
```

## Run the application

```powershell
dotnet run --project src/EntityTracker.Wpf/EntityTracker.Wpf.csproj
```

The application window opens to the Portfolio view.

## First steps

1. **Create a Project** — On the Portfolio, click **New project** and give it a name.
2. **Create a Tracker** — Open the Project dashboard and create a Tracker (e.g. "Core").
3. **Import a schema** — Open **Schema Synchronization**, choose **Get SQL Query**, run the
   query in your PostgreSQL instance, export the result as a semicolon-delimited UTF-8 CSV,
   then choose **Choose CSV and Review** and apply.
4. **Track progress** — Open the Tracker overview, select entities, and update their
   development status. Use filters and search to find work items.
5. **Review reports** — Open **Reports** to see status distribution, implementation history,
   and blocker trends.

## Where data lives

All application data is stored per-user under:

```
%LOCALAPPDATA%\EntityTracker\
```

This includes the SQLite database, settings, backups, and logs. Running from source and from a
published ZIP share the same data for the same Windows user.

## Run tests

```powershell
dotnet test EntityTracker.slnx --no-build --no-restore
```

## Troubleshooting

| Symptom | Fix |
| --- | --- |
| `NU1100` restore errors | `dotnet nuget locals all --clear` then restore again |
| `global.json` SDK not found | Install the exact SDK version from the [.NET download page](https://dotnet.microsoft.com/download/dotnet/10.0) |
| Build fails on WPF projects | Confirm you are on Windows; WPF is not supported on other platforms |
| Application opens but shows no data | Data is per-user; check `%LOCALAPPDATA%\EntityTracker\entity-tracker.db` exists |

## Further reading

- [Development, build, and run guide](DEVELOPMENT.md) — full reference for publishing, CI,
  screenshots, and schema import details.
- [Architecture rules](architecture/ARCHITECTURE.md) — layering and dependency constraints.
- [Schema CSV contract](importing/schema-csv-contract-v1.md) — exact CSV format for imports.
- [Recovery guide](operations/RECOVERY.md) — backup, restore, and data recovery procedures.
