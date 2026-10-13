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
git clone https://github.com/Tosuma/EntityTracker.git
cd EntityTracker
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

## Continuous integration

GitHub Actions runs the same Release restore, build, and test commands for every pull request and
every push to `main` or an `app-vX.Y.Z` tag. A newer run for the same pull request or branch cancels an older run that is
still in progress. The workflow uses a Windows runner and the SDK selected by `global.json`; it
does not require a database, SharePoint access, organization credentials, or repository secrets.

Successful pushes to `main` or an app release tag also run:

```powershell
.\scripts\Publish-Windows.ps1 -Configuration Release
```

The resulting `artifacts\EntityTracker-win-x64.zip` is uploaded to that workflow run as the
`EntityTracker-windows-x64-self-contained` artifact and retained for 14 days. Pull requests never
run the packaging job, so it appears as skipped on a pull-request run. Merging to `main` starts a
separate push run where packaging is enabled. GitHub Releases and external deployment targets are
not part of this workflow.

## Release an installed-app update

### Choose the version number

Release tags are `app-vMAJOR.MINOR.PATCH`. Decide the bump by what the release asks of the people
using EntityTracker, not by how large the change is. Ask: **can everyone keep working without
updating, and without doing anything by hand?**

- **Major** (`2.0.0`): someone is forced to update or to act. Use it when the release:
  - changes the shared Project sync format (`ProjectSnapshot.CurrentFormatVersion`), so older
    versions can no longer sync with a Project this version has written. This counts even though the
    app tells colleagues an update is required: they are still forced to update;
  - changes other shared data so older versions can't read it (for example the Project repository
    layout or tombstones);
  - removes something people rely on, or changes what it means: a feature, a status, a report
    audience, accepted CSV input, or tables and columns that saved Help & SQL queries use;
  - needs a manual step (re-import, re-clone, export) or a new install requirement (Windows or .NET
    version, install location, update channel).
- **Minor** (`1.11.0`): something new, nothing forced. New features and options; older versions keep
  working alongside, including with data they simply ignore. The sync format version does not change.
- **Patch** (`1.10.1`): nothing new. Fixes, wording, colours, layout and performance.

The local database (`SqliteDatabase.CurrentSchemaVersion`) and settings (`EntityTrackerSettings.CurrentVersion`)
are upgraded on each machine when the new version starts and force no one else to update, so a change
to them alone does not need a major release.

Tags up to `app-v1.10.0` did not follow this rule (`app-v1.8.0` raised the sync format in a minor
release); they stay as they are.

### Publish the release

1. Merge the update to `main`. Wait for its Windows CI build, tests, and package job to pass.
2. In your development checkout, update `main` and run the release script. Choose `Major`, `Minor`,
   or `Patch`, and write the release message that will be stored in the annotated Git tag:

   ```powershell
   git switch main
   git pull --ff-only
   .\scripts\New-AppRelease.ps1 -Bump Minor -Message "Release message" -MainCiPassed
   ```

   `-MainCiPassed` confirms that you checked the **main push** run on GitHub, including the package
   job. The script requires a clean local `main` matching `origin/main`; it reads the latest release
   tag from `origin`, creates the next tag, and pushes only that tag. The first tag is always
   `app-v1.0.0`. Later tags increment the selected component and reset lower components. If a push
   fails, the script leaves the local tag in place and prints a command to retry after you inspect
   the failure. Never move or reuse a pushed release tag.

3. Confirm the tag's CI run passes. The tag becomes visible to installed apps as soon as it is
   pushed, so only push a verified release. Protecting `main` with required pull requests does not
   prevent a separate tag push; a tag ruleset can restrict who may create `app-v*` tags.

Run the release-script fixture tests with `.\scripts\Test-New-AppRelease.ps1`. CI also runs them.

The first `app-vX.Y.Z` tag must include the installer and updater scripts. Until it exists, the
first-install script reports that there is no approved release. Use a new higher version for every
update. Breaking shared Project snapshot changes must still increment and validate the snapshot
format version, because a release can appear while an older process is finishing an in-flight sync,
and they are released as a major version (see [Choose the version number](#choose-the-version-number)).

Colleagues clone the app repository once and run `.\scripts\Install-EntityTracker.ps1`. The
script fetches an approved tag into that existing clone, archives its source without switching
branches or touching local edits, builds a self-contained app, and installs it under
`%LOCALAPPDATA%\Programs\EntityTracker` with a Start Menu shortcut.

The build runs in Docker by default (`scripts/Invoke-DockerBuild.ps1`), so colleagues need Docker
Desktop or Rancher Desktop but no .NET:

- **Image:** the staged release's `global.json` selects the image `mcr.microsoft.com/dotnet/sdk:<version>`.
  It is a Linux container, building the WPF app for Windows with `EnableWindowsTargeting`.
- **Build:** the staged source folder under `%LOCALAPPDATA%\EntityTracker\updater` is mounted at
  `/src`, and the container runs `scripts/Publish-Windows.ps1` there. The output lands in
  `artifacts/publish/win-x64` exactly as a local build's would, and the installer installs it as
  before. The container is removed afterwards; NuGet packages stay in the `entitytracker-nuget`
  volume.
- **Checks first:** the script stops with a plain message when Docker isn't running, uses Windows
  containers, or can't write to the folder.
- **Without Docker:** `-BuildWith Local` builds with the .NET SDK on the machine instead.

`.\scripts\Uninstall-EntityTracker.ps1` removes what the installer created: the app folder, any
`EntityTracker.new.*`/`EntityTracker.old.*` folders, the Start Menu shortcut and the updater's build
folders. It keeps `%LOCALAPPDATA%\EntityTracker` unless run with `-RemoveData`, refuses while the app
is running, and never touches the source clone. `.\scripts\Test-Uninstall.ps1` checks it against a
fake install; CI also runs it.

`Publish-Windows.ps1` therefore runs both on Windows and in that Linux container. CI checks the
container build in its Linux job, and `.\scripts\Test-DockerBuild.ps1` checks the script against a
fake `docker`. An exact release can be
selected with `-Tag app-v1.0.0` for recovery. The app's update notification uses the same installer
through a separate updater window after the app closes. Neither script clones or initializes a
repository, configures Git, or manages credentials.

The updater scripts (`Update-EntityTracker.ps1`, `Install-EntityTracker.ps1` and
`Install-Common.ps1`) are read from the release being installed, not from the installed app: the
app verifies the tag against origin the same way the installer does and runs that tag's
`scripts/` copies. A fix to the updater therefore applies on the update that ships it. If the
release cannot be read, the app falls back to the scripts installed with the current version.
Apps from before this change still run their installed updater once, for the update to the first
release that contains it. Run `.\scripts\Test-UpdaterExitCode.ps1` to check that the updater
reads the install's exit code under Windows PowerShell; CI also runs it.

The updater window shows which version of the updater is running and whether it came from the
release being installed or, after a fallback, from the current app. The installer's first output
line names the same version; run by hand from a clone, it reports `git describe` for the checkout,
for example `EntityTracker installer app-v1.5.0 (source checkout)`. CI checks both with
`.\scripts\Test-InstallerVersion.ps1`.

The ZIP includes EntityTracker's `LICENSE.txt`, the runtime dependency inventory in
`THIRD-PARTY-NOTICES.txt`, common license texts, and the upstream .NET and native graphics
third-party notices. Packaging fails if any required legal file cannot be collected.

When CI fails, inspect the first failed step:

- **Set up .NET** — confirm `global.json` selects an SDK available from Microsoft.
- **Restore** — check NuGet availability and package versions.
- **Build** — inspect compiler, analyzer, and WPF XAML diagnostics.
- **Test** — reproduce with the Release test command above; tests must not depend on local data,
  SharePoint, credentials, or execution order.
- **Publish Windows package** — run the publishing command locally and inspect its output.
- **Upload Windows package** — confirm the publishing script created the expected ZIP path.

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

## Generate README screenshots

EntityTracker includes a Windows-only WPF screenshot utility for keeping the README images
repeatable. From the repository root, generate a preview set with:

```powershell
.\scripts\Generate-ReadmeScreenshots.ps1
```

The default preview is written to `artifacts\readme-screenshots`, with complete `dark` and `light`
subdirectories, and is ignored by Git. Screenshots use the sample app version `1.0.0` by default;
pass `-Version` to show another version. Full-window screenshots are always 1920 × 1080, whatever
screen or display scaling you run the tool on; the tool stops if one is not. Dialogs and page-section
close-ups keep their own size. You can choose another preview directory without replacing
unrelated files:

```powershell
.\scripts\Generate-ReadmeScreenshots.ps1 -Output C:\path\to\preview
```

Inspect every preview image before replacing the tracked README set. Once approved, run:

```powershell
.\scripts\Generate-ReadmeScreenshots.ps1 -UpdateReadme
```

The update command renders and validates the complete manifest in Dark mode and then Light mode
before replacing the files owned by the generator under `images\dark` and `images\light`;
unrelated image assets are preserved. Each open screenshot window switches to the opposite
appearance and back before capture, exercising live Light-to-Dark and Dark-to-Light transitions.
The utility imports the repository's 125-entity synthetic schema, creates two Projects and three
related Trackers, creates fixed status and 90-day history data, and captures the portfolio, Project
dashboard, an illustrative upstream Git repository card with a sample sync duration,
Tracker copy review, overview, synchronization,
entity, progress, archive, destructive
confirmation, Help &amp; SQL, SQL-query, and Settings states. It uses a new SQLite database below the
operating-system temporary directory for each appearance and never reads or changes
`%LOCALAPPDATA%\EntityTracker`. The application may remain open while the screenshots are
generated. The linked Git card is an illustrative state backed by a directory inside the
temporary screenshot workspace. Only its displayed path is replaced with a generic example during
capture; the stored path remains the temporary directory. Generation does not create or modify a
Git repository or commit.

CI restores, builds, and tests the screenshot project as part of the solution, but deliberately
does not render or replace README images. Rendering remains an explicit local review workflow.

To create synthetic development statuses and progress history in an existing database, close
EntityTracker and run:

```powershell
.\scripts\Seed-ProgressDemo.ps1 -ConfirmReset `
  -DatabasePath "$env:LOCALAPPDATA\EntityTracker\entity-tracker.db" `
  -ProjectName "Production" -TrackerName "Schema" `
  -Days 90 -Seed 42
```

The Project/Tracker pair is matched case-insensitively, so same-named Trackers in different
Projects remain unambiguous. The command replaces statuses and progress history for that Tracker
only, without a backup; names, notes, dependencies, provenance, and archive state are preserved.
Add `-DemoNotes` to also give some entities that have no notes example Internal notes and Shared
notes, so the difference shows in the app and in Project reports; existing notes are never changed.
The README screenshots and sample reports use demo notes.

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

## Settings and current storage behavior

The **Settings** destination controls System, Light, or Dark appearance. The local
settings file also remembers the last valid active Project/Tracker context. SQLite remains the only
active provider; the application does not expose a remote connection or synchronization control.

Settings version 4 stores only appearance and active Project/Tracker context. Versions 1–3 are
read without modification; retired provider and SharePoint fields are ignored, and the next
legitimate settings save atomically writes version 4 without those fields.

## Local application data

EntityTracker stores runtime data for the current Windows user under:

```text
%LOCALAPPDATA%\EntityTracker\
```

The active files and directories are:

```text
entity-tracker.db    SQLite database
git-sync-links.v1.json  local Project-to-repository associations (when used)
settings.json        optional local appearance and last active Project/Tracker context
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

## Logging

### Where the logs are

EntityTracker writes one log file per day to `%LOCALAPPDATA%\EntityTracker\logs\entity-tracker-YYYYMMDD.log`
and keeps the newest 14. **Settings › About › Logs** shows the folder and has an **Open log folder**
button.

Each line looks like this:

```text
2026-10-13T07:41:07.1234567+00:00 [Information] (12) EntityTracker.Application.GitSync.ProjectGitSyncService [Sync 3f9a1c]: Sync finished in 812 ms: UpToDate, ...
```

The parts are:
- time, in UTC;
- level;
- managed thread id;
- category (the class that logged);
- the scopes the line was logged in;
- the message;
- for a failure, the whole exception on the lines below.

Every Git sync operation (Link, Import, Sync, Auto-sync, Unlink) runs in its own scope, such as
`Sync 3f9a1c`. Its Git commands, phases, conflicts and result all carry that scope.

### Levels

| Level | Used for |
|---|---|
| Critical | A crash: an unhandled exception that ends the app. |
| Error | Something failed and was handled, for example by telling the user. Logged with the exception. |
| Warning | An expected, recoverable failure, such as an unreadable settings file that falls back to defaults or a refused push that is retried. Also logged with the exception. |
| Information | What the app did: start and stop, each Git operation and its phases, Git commands that change something (fetch, push, commit, merge and the plumbing behind them), and cancellations the user asked for. |
| Debug | Detail for troubleshooting: read-only Git commands, lock waits, retries, and searches a newer search replaced. |

**Detailed logging:** by default the log holds Information and above. **Settings › About › Detailed
logging (for troubleshooting)** adds Debug, takes effect immediately and is kept in `settings.json`.

**Override:** set the environment variable `ENTITYTRACKER_LOG_LEVEL` (`Trace`, `Debug`,
`Information`, `Warning`, `Error` or `Critical`) to override the setting, for example in a support
session. The About page says when an override is active.

### Rules for code

- **Every caught exception is logged.** A `catch` block either:
  - logs, with the exception (Error, or Warning for an expected recoverable case);
  - or passes the exception on with `throw` (rethrow, or translate it and keep the original as the
    inner exception). The place that finally handles it logs it once.
  - Cancellation is not an error: it logs one Information line (Debug when a newer request or app
    shutdown caused it).
  - The only exception is the log itself, which can't log its own failure. Such a block says why with
    a `// Not logged: <reason>` comment.

  `LoggingCoverageTests` parses every `catch` block in `src` and fails on one that does none of
  these.
- **Handled Git sync failures:** a UI catch that shows such a failure to the user calls
  `logger.LogHandled(exception, ...)`. The sync operation has already logged the failure in full, so
  this writes only a line that points back to it. A failure that wasn't logged yet is logged in full.
- **Message templates, never interpolation:**
  - use `LogError(exception, "Sync of Project {ProjectId} failed.", id)`, not `$"..."`;
  - name what was being done and the IDs involved.
- **No user content:** never log notes, Filter active, comments or other text users wrote. Merge
  conflicts are logged by field path only, never by value.
- **Secrets:** Git's error output can contain remote URLs with credentials. It is never shown in the
  UI. `SystemGitTransport` logs it (the first 4 KB) only after `LogRedaction.Redact`, which removes
  credentials in URLs, token and password query values, and `Authorization`/`Bearer` values. Pass
  anything that could contain a URL through `LogRedaction` too.
- **Getting a logger:**
  - classes the service container creates take an `ILogger<T>?` constructor parameter that defaults
    to `NullLogger<T>.Instance`;
  - views and other classes it doesn't create use
    `private static readonly ILogger Logger = AppLogging.CreateLogger<T>();`.

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
