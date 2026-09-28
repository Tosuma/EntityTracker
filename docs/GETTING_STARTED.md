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

## Install for everyday use

```powershell
git clone https://github.com/Tosuma/EntityTracker.git
cd EntityTracker
.\scripts\Install-EntityTracker.ps1
```

The script builds the newest approved `app-vX.Y.Z` release on your computer, installs it at
`%LOCALAPPDATA%\Programs\EntityTracker`, creates a **Start Menu** shortcut, and opens it. Keep
the source clone: it is used to check and build later releases. Git access and .NET package restore
must work on this computer.

The app checks for new releases at startup, every 15 minutes, and before each Project Git sync.
When an update is available, finish any open edit and choose **Close and update**. The updater
window shows build progress and reopens the app when complete. If the build fails, use **Retry
update** or exit; the older app remains blocked. If the release check is unavailable, local work
continues but Project Git sync pauses until a check succeeds.

If restore fails with `NU1100` errors, check network access to NuGet, then retry:

```powershell
dotnet nuget locals all --clear
.\scripts\Install-EntityTracker.ps1
```

## Run from source for development

```powershell
dotnet run --project src/EntityTracker.Wpf/EntityTracker.Wpf.csproj
```

Source runs do not enforce installed-app updates. They share the same per-user data with the
installed app, so close one before opening the other.

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

This includes the SQLite database, settings, backups, and logs. The installed app lives separately
under `%LOCALAPPDATA%\Programs\EntityTracker`. Updating it does not replace the database or your
user-managed Project repositories. Startup applies database migrations after making a backup.

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
| Installer reports no approved release | Ask the developer to publish the first `app-vX.Y.Z` tag |
| Update build fails | Use the updater's error log; restore source-clone access and NuGet connectivity, then retry |
| Local work opens but Project sync is paused | Restore access to the app source clone and its Git origin, then select **Retry check** |

## Further reading

- [Development, build, and run guide](DEVELOPMENT.md) — full reference for publishing, CI,
  screenshots, and schema import details.
- [Architecture rules](architecture/ARCHITECTURE.md) — layering and dependency constraints.
- [Schema CSV contract](importing/schema-csv-contract-v1.md) — exact CSV format for imports.
- [Recovery guide](operations/RECOVERY.md) — backup, restore, and data recovery procedures.
