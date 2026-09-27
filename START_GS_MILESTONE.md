# Start a Git-sync milestone

Choose one milestone below. Replace both placeholders in the prompt, then paste the prompt into a new agent conversation. Pasting the prompt explicitly starts implementation of **that milestone only**; this file does not start any milestone by itself.

| Milestone ID | Milestone file |
| --- | --- |
| `GS-01` | `docs/milestones/git-sync/gs_01_portable_project_snapshot.md` |
| `GS-02` | `docs/milestones/git-sync/gs_02_local_git_linking.md` |
| `GS-03` | `docs/milestones/git-sync/gs_03_existing_checkout_remote_sync.md` |
| `GS-04` | `docs/milestones/git-sync/gs_04_collaborative_merge.md` |
| `GS-05` | `docs/milestones/git-sync/gs_05_automatic_sync_and_hardening.md` |

## Prompt

```text
Implement {{MILESTONE_ID}} in this repository. This request authorizes implementation of this milestone only.

Read {{MILESTONE_FILE}}, docs/milestones/git-sync/00_README.md, docs/milestones/00_README.md, and docs/milestones/milestone_status.md. Inspect the current code, tests, architecture, and working tree before making changes. Read and follow the selected document's agent implementation prompt and acceptance criteria. Preserve unrelated working-tree changes.

Verify that all prerequisite GS milestones are actually complete. If a prerequisite is incomplete, stop and report what is missing without starting this milestone. Otherwise, implement the selected milestone fully, including its necessary code, migrations, UI, documentation, and meaningful tests. Keep the user-managed repository boundary: EntityTracker must not clone or initialize repositories, configure Git, or manage credentials.

Build the full solution and run the relevant tests. Fix failures caused by this milestone. Update the milestone document and cross-category status page to completed only after its acceptance criteria pass. Do not begin the next milestone or add features assigned to later milestones.

Report what changed, the verification results, and any remaining limitations.
```
