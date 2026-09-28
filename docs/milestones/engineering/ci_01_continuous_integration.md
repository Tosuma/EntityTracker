# Engineering Milestone CI-01 — Continuous Integration

## Independence

CI-01 is an engineering milestone, not a numbered product milestone. It may be implemented at any
time and does not depend on Milestone 13 or any SharePoint access, authentication, or credentials.

## Goal

Add a GitHub Actions workflow that automatically builds and tests EntityTracker for pull requests
and changes to `main`, then verifies and uploads the self-contained Windows package from successful
`main` builds.

## Tasks

- Add `.github/workflows/ci.yml` with triggers for pull requests and pushes to `main`.
- Run on `windows-latest` and install the SDK selected by `global.json`.
- Grant only read access to repository contents unless a future reviewed requirement needs more.
- Add per-branch workflow concurrency and cancel superseded in-progress runs.
- Restore the complete solution once, build Release with `--no-restore`, and run every test project
  with `--no-build --no-restore`.
- Keep ordinary CI independent of databases, SharePoint, credentials, and external organization
  services.
- On successful pushes to `main`, run `scripts/Publish-Windows.ps1 -Configuration Release` and
  upload `artifacts/EntityTracker-win-x64.zip` as a GitHub Actions artifact.
- Give the artifact an unambiguous name and a finite retention period; do not create a GitHub
  Release or publish anywhere outside the workflow run.
- Add a workflow-status badge to `README.md` only after the workflow exists and has completed
  successfully on `main`.
- Document the CI commands, triggers, artifact, and common failure troubleshooting.

## Failure and security behavior

- A restore, compiler, analyzer, test, WPF XAML, publish, or packaging failure fails the relevant
  job and prevents package upload.
- Packaging runs only after the build-and-test job succeeds on `main`.
- Pull requests from forks require no repository or organization secrets.
- The workflow does not run live SharePoint integration tests. Any future organization-backed smoke
  suite remains separately gated and must never expose credentials to untrusted pull requests.
- Third-party actions are pinned to reviewed major versions or immutable commits according to the
  repository's chosen dependency policy.

## Tests and verification

- Open a pull request with a passing change and verify restore, Release build, and all tests run.
- Deliberately introduce a compiler failure and a test failure on a temporary branch and verify each
  prevents a green workflow.
- Push a passing change to `main` and verify the Windows ZIP is produced and downloadable from the
  workflow run.
- Inspect the ZIP by extracting it and starting `EntityTracker.Wpf.exe` on Windows.
- Verify pull-request runs do not publish packages and no job requests or prints secrets.
- Verify superseding commits cancel the older in-progress run for the same branch.

## Acceptance criteria

- Every pull request and push to `main` automatically restores, builds the complete Release
  solution, and runs the complete test suite on Windows.
- Compilation and test failures make the workflow fail visibly.
- Successful `main` runs produce a downloadable self-contained Windows x64 ZIP artifact.
- Package upload never runs for pull requests or after a failed build/test job.
- The workflow requires no SharePoint connection, organization credentials, or other secrets.
- Workflow permissions are least-privilege and concurrent runs are bounded.
- A real passing build-status badge is present in the README.
- Local build, test, and publish commands remain the same as the documented CI commands.
