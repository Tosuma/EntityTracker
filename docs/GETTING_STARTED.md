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

If an installed app closes for an update but does not reopen, close any updater window and run the
installer from an up-to-date source checkout in an open PowerShell terminal:

```powershell
git switch main
git pull --ff-only
.\scripts\Install-EntityTracker.ps1
```

This builds the newest approved release using the checkout's installer script. The application
database remains under `%LOCALAPPDATA%\EntityTracker`; reinstalling does not replace it. In
particular, `app-v1.0.0` bundled an installer that could wait for a hidden prompt during an in-app
update, so use this manual path to move from `app-v1.0.0` to `app-v1.0.1` or later.
If Windows reports that the install folder is in use even after closing EntityTracker and the
updater, restart the computer and rerun the installer before opening the app.

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
5. **Share a report** — On the Project dashboard, open **Project report**, choose the Trackers and
   who it is for, and export one offline web page with status distribution, implementation
   history, blocker trends, the dependency graph and every entity.

### Assign developers

Choose **You in this Project** in Settings to identify your local Developer for the selected
Project. The choice stays on this installation; you can change or clear it at any time. Use
**Assign me** in active entity details to assign immediately, or in the entity editor to stage
the assignment until **Save Changes**. The action opens guidance to Settings when no available
Developer is selected. Other current Developers remain assigned.

Open an entity or create a new one, then search the Project developer picker and select one or
more available developers. Create new developers from the Project Developers page. Use **Edit** in
an active entity's details pane to open its editor. Entity details show all current assignments
and up to three timeline entries; **View full history** opens the
complete timeline in the details pane, and **Back** returns to the summary. Current assignments
appear newest first, followed by past assignments ordered by when they ended, newest first.
Removing a developer ends their current period; assigning them again starts a new one. Retiring a
developer from the Project dashboard ends all their open assignments, including on archived
entities. The active and archived overviews show each current Developer separately. Use the
**Responsible dev** column filter to find one or more Developers or **(Blank)** for unassigned
entities. All overview column filters start with no checked values, which shows all rows. Check
the values you want and choose **Apply**; **Clear filter** unchecks them and keeps the menu open.
Ordinary search includes current Developer initials and display names by default; turn off
**Search responsible names** in Settings to search entity names only. **Search dependency
names** remains a separate search mode.

### Sync a copied tracker

On the Project dashboard, choose **Sync from source** on a tracker created with **Copy tracker**.
Review each entity, dependency, requested priority, and group difference. For each dependency,
choose **Keep dependency** or **Remove dependency**. To keep separate dependencies added in
each tracker, choose **Keep dependency** on each row. For other changes, choose the source value
or keep this tracker's value. Every change needs a choice
before **Apply sync** is enabled. Removed entities can be archived in the copied tracker or kept;
their progress and history remain available. Existing status, notes, and developer assignments
stay with the copied tracker. A declined source change is not offered again unless the source changes;
new destination edits can still appear in the next review.

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
| App closes for an update but does not reopen | Run the installer from the updated source checkout as shown above |
| Local work opens but Project sync is paused | Restore access to the app source clone and its Git origin, then select **Retry check** |

## Further reading

- [Development, build, and run guide](DEVELOPMENT.md) — full reference for publishing, CI,
  screenshots, and schema import details.
- [Architecture rules](architecture/ARCHITECTURE.md) — layering and dependency constraints.
- [Schema CSV contract](importing/schema-csv-contract-v1.md) — exact CSV format for imports.
- [Recovery guide](operations/RECOVERY.md) — backup, restore, and data recovery procedures.
