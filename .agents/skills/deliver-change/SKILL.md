---
name: deliver-change
description: Take a change in EntityTracker from an issue to a reviewed pull request. Read the given GitHub issue, or draft one and get the user's approval; branch from main; implement with tests and docs; run the required verification and screenshots; open a pull request that closes the issue; wait for its CI to pass. Stops at a green pull request. It merges only when the user asked for that, by handing over to merge-and-release. Use for "implement issue #N", "fix …", "add …" and "make a PR for …".
---

# Deliver a change: issue → branch → implementation → pull request

Goal: a focused pull request with green CI that closes its issue. Read `AGENTS.md` first; its rule
on merging and releasing applies throughout.

## 1. The issue

- **Given an issue number:** read it with `gh issue view <N> --comments`. Its acceptance criteria are
  the scope. If they are unclear or conflict with the code, ask before implementing.
- **No issue yet:**
  1. Draft one: a title, a **Problem** (what is wrong or missing, for whom), a **Scope** (what changes;
     what is out of scope), **Acceptance criteria**, and labels (`enhancement`, `bug`, `documentation`,
     `accessibility`, …).
  2. **Show the draft to the user and wait for approval** before running `gh issue create`.
  3. Use `--body-file` so the Markdown survives the shell.
  4. The one exception is the fix for a broken `main` that `merge-and-release` asks for. Create that
     issue directly and say so in the report.
- **Large work:** propose a split into phased issues (one pull request each, stacked) and agree it with
  the user before creating them.

## 2. The branch

```bash
git switch main && git pull --ff-only
git switch -c <issue#>-<short-slug>        # e.g. 113-agent-workflow-skills
```

- Check `git status` first. Leave the user's untracked files alone and never stash or delete them.
- For stacked phases, branch each phase from the previous phase's branch. Its pull request targets
  that branch, not `main`.

## 3. Implement

- Make the smallest coherent change that meets the acceptance criteria; avoid unrelated edits.
- Follow the architecture rules (`docs/architecture/ARCHITECTURE.md`):
  - Business rules stay out of WPF view models and code-behind.
  - Domain and Application stay free of WPF and Infrastructure types.
- Match the surrounding code's naming, comments and idioms. Use existing styles, resources and helpers
  before adding new ones.
- **Tests:** add behaviour-focused tests for new or changed behaviour.
  - `PresentationConfigurationTests` checks XAML structure.
  - View-model tests use the `ShellHarness`.
  - In asynchronous tests, wait until **every** value you assert has settled, not just the first. CI
    flakes in this repository came from asserting a second value too early.
- **Docs:** update the README, `docs/` and the design guide wherever the behaviour or look changes.
- **Choices for the user:** when an implementation choice has several reasonable answers the user
  should own (UX, colours, naming), ask. Show visual options side by side on a page.
- **Phase fixes:** fix a problem in the phase or branch it belongs to, then merge that branch up the
  stack, rather than fixing everything on the top branch.

## 4. Verify

1. Run the three required commands from `AGENTS.md`, in the background, and read the summary lines.
   Fix every failure. If a test fails only in a run that overlapped with something else (a browser
   stealing focus), rerun it on its own once, and say so.
2. For a UI change that shows in the README screenshots:
   1. Preview with `scripts/Generate-ReadmeScreenshots.ps1`.
   2. Inspect the changed captures in **both** themes.
   3. Publish with `-UpdateReadme`.
   4. Check that each theme still has the number of captures `AGENTS.md` states.
3. To check something the screenshots don't cover (for example a narrow window), make a temporary,
   uncommitted change to the screenshot tool and revert it afterwards.

## 5. The pull request

1. Stage the intended files only (`git add -u`, plus new files by name), commit with a message that
   says what changed and why, and push with `git push -u origin <branch>`.
2. Write the body to a file and run `gh pr create --base <main or the parent phase branch>
   --body-file …`. The body contains:
   - the problem and a summary of the changes;
   - deviations from the issue or plan;
   - the tests added and the verification results;
   - a note on the screenshots;
   - `Closes #<issue>`;
   - the session's pull-request attribution line.

## 6. CI

- Wait for the pull request's checks on the **current head commit**: "Build and test" and "Build in
  Docker as the installer does".
  - `gh pr checks <N>` shows them.
  - Confirm the run's `headSha` matches `gh pr view <N> --json headRefOid`.
  - Wait with a background watcher, not by polling in the foreground.
- **On failure:**
  1. Read `gh run view <run-id> --log-failed`.
  2. Fix the cause on the same branch.
  3. Push, and wait again.
  - Don't rerun a real failure hoping it passes.

## 7. Stop, or hand over

- Report the issue, branch, pull-request link and CI result, and any decisions or deviations.
- **Do not merge.** Continue with `merge-and-release` only if the user's request included merging (for
  example "… and merge it when done" or "merge and release"), or once they tell you to.
