# Agent instructions

These instructions are shared by Claude Code (through `CLAUDE.md`, which imports this file) and Codex.
The skills live in `.agents/skills/`; `.claude/skills` is a symlink to that folder, so both agents use
the same files. Edit them in one place only.

## The application

- A Windows desktop application in C# and WPF (XAML), with business logic kept out of the UI layer.
- The UI uses Microsoft's **built-in Fluent theme for WPF**, not the third-party WPF UI library.
- Icons are **Microsoft Fluent System Icons**. Check how they are integrated (`Themes/EntityTrackerIcons.xaml`
  and `Controls/Icon`) before adding or replacing any; don't confuse them with Segoe Fluent Icons or
  assume WinUI or React controls exist.
- The window has a persistent sidebar and a work area that changes with the selected page.

Before changing code, read `CONTRIBUTING.md`, `docs/DEVELOPMENT.md` and
`docs/architecture/ARCHITECTURE.md`. Before changing the UI or reports, also read
`docs/design/DESIGN_GUIDE.md` and `docs/design/FLUENT_UI_DIRECTION.md`. Every colour is defined once in
`src/EntityTracker.Theme/ColorScheme.cs`.

## Skills

| Skill | Use it when |
|---|---|
| `ui-review` (`.agents/skills/ui-review/SKILL.md`) | The user asks for a UI, UX, design, consistency, accessibility or navigation review. Assessment only: it writes one new report under `docs/review/` and changes nothing else. |
| `deliver-change` (`.agents/skills/deliver-change/SKILL.md`) | The user asks to implement an issue, fix or add something, or "make a PR". It goes from the issue to a pull request with green CI, then stops. |
| `merge-and-release` (`.agents/skills/merge-and-release/SKILL.md`) | The user asks to merge a pull request or to release, or asked for that together with a change ("… and merge it when done"). |

## Merging and releasing need the user's say-so

**Never** merge a pull request, push to `main`, or create or push a release tag unless the user asked for
it in the current conversation, for that piece of work. A finished, green pull request is not
permission to merge it, and permission for one pull request does not cover another. The only exception
is the fix pull request that `merge-and-release` opens when `main` fails after a merge the user asked
for.

## Working in the repository

- **Verification.** Before opening a pull request, run the required commands:

  ```powershell
  dotnet restore EntityTracker.slnx
  dotnet build EntityTracker.slnx --configuration Release --no-restore
  dotnet test EntityTracker.slnx --configuration Release --no-build --no-restore
  ```

  The full test run takes about 12 minutes (Infrastructure alone 7-9), so run it in the background.
  The WPF tests alone take about 1.5 minutes.
- **Screenshots.** For a UI change shown in the README, regenerate the screenshots with
  `scripts/Generate-ReadmeScreenshots.ps1`:
  - Preview first: the output goes to `artifacts/readme-screenshots`.
  - Inspect the changed captures in both themes.
  - Publish with `-UpdateReadme`. It takes over 10 minutes, so run it in the background.
  - Run it with PowerShell; `powershell` is not on Git Bash's path.
  - The manifest must stay at 50 captures per theme (update this number when a capture is added on purpose).
  - Text-box placeholders never appear in captures.
- **Branches and commits.**
  - Branch from an up-to-date `main` as `<issue#>-<short-slug>`.
  - Keep commits focused, and end commit messages and pull-request bodies with the attribution lines the
    session gives.
  - Never commit untracked local files the user didn't ask for (for example `docs/review/`). Stage files by
    name or with `git add -u`, not `git add -A`.
  - Pull requests are merged with merge commits, and branches are not deleted on merge.
- **Worktrees.** Work in the main checkout and switch branches there. When a second checkout is
  unavoidable, create it inside the repository as `.worktrees/<name>`, never beside it: a folder
  outside the repository is outside the agent's sandbox. `.worktrees/` must be listed in
  `.git/info/exclude` (add it if it isn't), so it never shows in `git status` or a commit. Remove the
  worktree when done.
- **Line endings.** Files mix LF and CRLF with `core.autocrlf=true`; the "LF will be replaced by CRLF"
  warnings are harmless.
- **GitHub CLI.** `gh` is installed at `C:\Program Files\GitHub CLI`. In PowerShell, refresh `PATH` first:
  `$env:Path = [System.Environment]::GetEnvironmentVariable('Path','Machine') + ';' + [System.Environment]::GetEnvironmentVariable('Path','User')`.
  In Git Bash, add `/c/Program Files/GitHub CLI` to `PATH`.
- **Symlinks.** `.claude/skills` is a Git symlink. The clone needs `core.symlinks=true` and Windows
  symlink support (Developer Mode or an elevated shell); otherwise the link checks out as a small text
  file.

## Choosing for the user

When a change has several reasonable answers that the user should own (UX, colours, naming, what to
cut), ask instead of guessing. For a visual choice, show the options side by side on a page. Decisions
with a clear convention in the repository don't need a question.
