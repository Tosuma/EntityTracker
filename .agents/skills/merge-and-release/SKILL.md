---
name: merge-and-release
description: Merge an EntityTracker pull request into main and publish an app release, only when the user has asked for it. Check that the PR is up to date and its CI is green, merge it with a merge commit, and wait for main's CI including the package job. Rerun a failure once; if it fails again, open, merge and release a fix PR. Once main is green, choose the version bump, write a user-facing message, tag with New-AppRelease.ps1 from a clean worktree, check the tag's CI and close the linked issues. Skips the release when nothing that installed apps use has changed. Handles stacked PRs one at a time, in order. Use for "merge PR #N", "merge it", "merge and release", or a change requested "and merge when done".
---

# Merge a pull request and release

Read `AGENTS.md` first. **Run this skill only when the user asked to merge, in this conversation, for
these pull requests.** A green pull request is not permission. Permission for one pull request does not
cover another, except the fix pull request in step 3.

For several pull requests, or a stack of phases, run the whole loop (steps 1–5) for **one** pull request
at a time, in order. Finish its release and tag CI before starting the next.

On Windows, put `gh` on `PATH` first (see `AGENTS.md`). Wait for CI with background watchers, not by
polling in the foreground.

## 1. Before merging

1. **Base:** the pull request must target `main`. For a stacked pull request whose parent has just
   merged, retarget it with `gh pr edit <N> --base main`.
2. **Up to date:**
   - Merge `origin/main` into the branch with a merge commit; never rebase a shared branch.
   - Resolve conflicts. Regenerated screenshots take the branch's version, then regenerate them if the
     content changed.
   - Push. Switch branches in the main checkout; only when it is busy, use a worktree under
     `.worktrees/` (see `AGENTS.md`).
3. **Green on the current head:**
   - `gh pr checks <N>`: "Build and test" and "Build in Docker as the installer does" pass.
   - The run's `headSha` equals `gh pr view <N> --json headRefOid`. After any push, wait again.
   - `gh pr view <N> --json mergeStateStatus` is `CLEAN`.
   - If CI fails, fix it on the branch first (see `deliver-change`, step 6).

## 2. Merge

```bash
gh pr merge <N> --merge                         # merge commit, as the repository does
gh pr view <N> --json mergeCommit --jq .mergeCommit.oid
```

Keep the merge commit SHA, and don't delete the branch.

## 3. Wait for main's CI

1. Find the `push` run for the merge commit:
   `gh run list --branch main --event push --commit <sha> --json status,conclusion,databaseId`.
2. Wait until it completes. All three jobs must succeed, **including "Package Windows application"**:
   `gh run view <id> --json jobs`.
3. **If it fails:** rerun the failed jobs once with `gh run rerun <id> --failed`, and wait again.
4. **If the rerun fails too:**
   1. Diagnose from `gh run view <id> --log-failed`.
   2. Fix it with `deliver-change`: create an issue for the failure (no draft approval in this case;
      say so in the report), a branch from `main`, the fix with a test if possible, and a pull request
      with green CI.
   3. Merge the fix pull request under the same permission (steps 1–2), and wait for main's CI on the
      new merge commit.
   4. Don't release from a red `main`. If the fix's own main CI also fails twice, **stop**. Report what
      failed and what you tried; don't loop.

## 4. Release (only once main is green)

0. **Is there anything to release?** A tag makes every installed app offer an update, so release only
   when the app or its installer changed. Compare with the latest tag:
   `git diff --stat <latest-tag> origin/main`.
   - **Skip the release** when only these changed:
     - agent files (`AGENTS.md`, `CLAUDE.md`, `.agents/`, `.claude/`);
     - documentation (`docs/`, `README.md`, `CONTRIBUTING.md`) and README screenshots (`images/`);
     - tests (`tests/`) or CI (`.github/`).
   - Say in the report that no release was made, and why. The changes ship with the next release.
   - Changes to `src/`, `scripts/`, `global.json` or the build files need a release. When it isn't
     clear whether a change reaches installed apps, ask.
1. **Choose the bump** with the rules in `docs/DEVELOPMENT.md` ("Choose the version number"). Judge
   by what the release asks of users, not by its size:
   - **Major:** someone is forced to update or act (sync format, shared data, removed behaviour, a manual
     step).
   - **Minor:** something new, nothing forced.
   - **Patch:** nothing new: fixes, wording, colours, layout, performance.
2. **Write the message** for users, in the style of earlier tags (`git tag -n9 <latest-tag>`):
   - Start with a short headline sentence, then say what changed and where to find it.
   - Leave out code, internal names and pull-request numbers.
3. **Tag from a clean `main`.** The release script refuses a working tree with untracked files, and
   the user may keep untracked files locally (`docs/review/`, …). Never delete or commit those.
   - If `git status --porcelain` is empty on an up-to-date `main` in the main checkout, tag there.
   - Otherwise, tag from the worktree `.worktrees/release` inside the repository, never from a folder
     beside it:

   ```powershell
   git worktree add .worktrees/release main      # or reuse it, then: git -C .worktrees/release pull --ff-only
   Set-Location .worktrees/release
   .\scripts\New-AppRelease.ps1 -Bump <Major|Minor|Patch> -Message "<message>" -MainCiPassed
   ```

   The script reads the latest tag from `origin`, creates the next `app-vX.Y.Z` and pushes only that
   tag. Never move or reuse a pushed tag.
4. **Wait for the tag's CI:** `gh run list --branch app-vX.Y.Z`. All jobs must pass, including the
   package job. If it fails, report it: the tag is already visible to installed apps.
5. Remove the worktree, if you made one, when the series is finished (`git worktree remove .worktrees/release`).

## 5. Close out

- **Issues:** `Closes #N` links have not auto-closed issues in this repository. Check them with
  `gh issue view <N> --json state`, and close any still open:
  `gh issue close <N> --reason completed --comment "Done in #<PR>, released in app-vX.Y.Z."`.
- **Report:** the pull request, merge SHA, main and tag CI results, the tag, the bump and why, and the
  release message.
- **Next pull request:** for a stack, continue with the next one (retarget, update, wait for CI, merge,
  …).
