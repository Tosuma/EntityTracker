# Contributing to EntityTracker

Thank you for considering a contribution. EntityTracker intentionally keeps its business logic
independent of WPF, SQLite, CSV libraries, charting, and future SharePoint infrastructure. Changes
should preserve those boundaries and remain focused on a concrete requirement.

## Before you start

- Read the [development guide](docs/DEVELOPMENT.md).
- Read the [architecture rules](docs/architecture/ARCHITECTURE.md).
- For UI or reporting changes, read the [design and color guide](docs/design/DESIGN_GUIDE.md).
- Check the [roadmap](docs/milestones/00_README.md) and
  [milestone status](docs/milestones/milestone_status.md).
- For a substantial change, open or reference an issue so behavior and scope can be agreed before
  implementation.

## Development workflow

1. Fork the repository and create a focused branch from `main`.
2. Make the smallest coherent change that satisfies the agreed behavior.
3. Add or update behavior-focused tests for new or changed behavior.
4. Update user, development, architecture, or operational documentation when behavior changes.
5. Run the complete verification commands below.
6. Open a pull request explaining the problem, solution, tests, and any compatibility impact.

Avoid broad rewrites, speculative abstractions, and unrelated formatting changes in the same pull
request.

## Required verification

```powershell
dotnet restore EntityTracker.slnx
dotnet build EntityTracker.slnx --configuration Release --no-restore
dotnet test EntityTracker.slnx --configuration Release --no-build --no-restore
```

The complete solution must build without errors, and all existing milestone regression tests must
continue to pass.

GitHub Actions runs these same Release commands for pull requests and pushes to `main`. Pull
requests validate the solution only; a successful push to `main` additionally creates the
self-contained Windows package described in the
[development guide](docs/DEVELOPMENT.md#continuous-integration). No credentials are required for
ordinary CI, including pull requests from forks.

## Architecture expectations

- Domain must remain independent of Application, Infrastructure, Reporting, and WPF.
- Application may depend on Domain but must not depend on WPF, SQLite, SharePoint, CSV, file-dialog,
  or charting types.
- Business rules, ranking, synchronization, and readiness do not belong in WPF view models or
  code-behind.
- Infrastructure-specific types must not leak into Domain or Application.
- Prefer focused interfaces with a current concrete responsibility; do not add generic repository,
  factory, service, or mediator frameworks for hypothetical future use.
- Preserve stable entity identity, imported facts, manual overrides, history, and dependency
  semantics unless the change explicitly and safely migrates them.

The authoritative details are in
[`docs/architecture/ARCHITECTURE.md`](docs/architecture/ARCHITECTURE.md).

## Pull-request checklist

- The change is limited to its stated scope.
- New behavior has meaningful tests.
- Existing tests pass.
- Project dependency direction remains valid.
- User-visible or operational changes are documented.
- No future milestone functionality was added incidentally.
- No credentials, tokens, private connection details, machine-specific paths, databases, logs,
  build output, or other generated artifacts are included.

## License

By contributing, you agree that your contribution will be licensed under the repository's
[MIT License](LICENSE).
