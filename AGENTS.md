# Repository Guidelines

## Working Style

- Act directly when the request is clear, low-risk, and within the named scope. Do not ask for permission before ordinary read-only checks, focused tests, builds, or implementation steps already authorized by the request.
- Ask only when requirements are materially ambiguous, multiple approaches have meaningful product trade-offs, or an action needs new authority.
- Prefer the smallest viable product correction. Apply KISS and YAGNI; do not add speculative features, abstractions, settings, compatibility layers, or infrastructure.
- Keep work tightly scoped to the named issue. Do not turn review observations or optional improvements into new issues without a concrete product defect or explicit user request.
- Lead with product behavior and user impact. Tests and tooling support the product; they are not independent deliverables.

## Git Safety

- Treat `main` as read-only. All repository changes go through a PR from `feature/<topic>`, `fix/<topic>`, or `chore/<topic>`.
- Before switching branches, merging, rebasing, or cleanup, verify `git status` and `git log -1 --oneline`.
- Create an early recoverable checkpoint for meaningful work. Prefer a small commit:
  - `WIP: <description> (checkpoint)`
  - `WIP: <description> (checkpoint; tests failing)` when applicable.
- Keep checkpoints and reviewed history intact unless the user explicitly authorizes rewriting.
- Never use destructive Git cleanup, force pushes, hard resets, or branch deletion without explicit approval.
- Use `gh` for all GitHub issue, PR, workflow, and release operations.

## Project Structure

- `src/WayfarerMobile`: .NET 10 MAUI application for Android and iOS.
- `src/WayfarerMobile.Core`: platform-independent models, algorithms, interfaces, helpers, and navigation logic.
- `tests/WayfarerMobile.Tests`: xUnit tests for the core and testable application seams.
- `docs`: Docsify user and developer documentation.
- The related Wayfarer backend repository is `C:\Users\stef\source\repos\Wayfarer`.

Keep portable domain and navigation behavior in `WayfarerMobile.Core`. Keep device APIs, permissions, lifecycle behavior, and platform services in the MAUI or platform-specific projects. Preserve existing MVVM ownership: views render and forward interaction, view models own presentation state, and services own application behavior.

## Reference Repositories and Navigation Contract

- The previous implementation at `C:\Users\stef\source\repos\Wayfarer.Mobile` and the Wayfarer backend are read-only references unless the user explicitly places them in scope.
- Use the previous app to verify API endpoints, DTOs, persistence shapes, geo/location algorithms, tile caching, navigation, trip display, and QR behavior. Do not copy obsolete architecture merely because it existed there.
- Preserve the documented navigation priority in `docs/12-Services.md`: user-defined Segment geometry, valid cached routing, online routing, then an honest direct-route fallback.
- Never fabricate connections between Places to make an incomplete route appear road-aware. Prefer an explicit straight-line distance/bearing fallback when authoritative or externally calculated geometry is unavailable.

## Build and Test Commands

- Restore tests and shared dependencies:
  - `dotnet restore tests/WayfarerMobile.Tests/WayfarerMobile.Tests.csproj`
- Build tests and the core project:
  - `dotnet build tests/WayfarerMobile.Tests/WayfarerMobile.Tests.csproj --configuration Release --no-restore`
- Run tests:
  - `dotnet test tests/WayfarerMobile.Tests/WayfarerMobile.Tests.csproj --configuration Release --no-build`
- Build Android when the changed seam requires MAUI/platform compilation:
  - `dotnet build src/WayfarerMobile/WayfarerMobile.csproj -f net10.0-android --configuration Release`
- Build iOS only on a suitable macOS/Xcode host and only when the risk requires it.
- The GitHub Android build is manual. Invoke it for project/framework/package changes; XAML, MAUI resources, startup or DI changes; Android platform services, permissions, manifests, or lifecycle work; materially MAUI-dependent map/navigation changes; and the final release candidate.
- Do not invoke the Android workflow for documentation, tests-only maintenance, or Core algorithms/DTO/state transformations already covered by reliable lower-seam tests unless a concrete app-compilation risk is identified.

## Coding Guidelines

- Prioritize clarity over cleverness and current requirements over future flexibility.
- Keep each class, service, view model, and method focused on one responsibility.
- Reuse established abstractions only when they already own the behavior; do not create a framework for one change.
- Use nullable reference types correctly and handle cancellation and platform failures explicitly.
- Follow existing C# conventions: four spaces, PascalCase public members/types, camelCase locals/parameters, and file names matching their primary type.
- Use a ViewModel for every page. Keep business logic out of code-behind; code-behind may own only view-specific behavior such as map gestures or animations.
- Prefer CommunityToolkit.Mvvm generators such as `[ObservableProperty]` and `[RelayCommand]` where they fit the existing ownership boundary.
- Use Syncfusion controls only where they provide clear current value and reduce custom UI complexity.
- Document public or non-obvious behavior. Avoid comments that merely repeat the code.
- Never commit tokens, server credentials, signing material, device identifiers, user location data, build outputs, coverage outputs, or diagnostic logs.

## Testing Guidelines

- Tests must protect product behavior or a concrete regression boundary. Do not add tests merely for completeness, line coverage, implementation details, or every theoretical permutation.
- Write the smallest failing test that reproduces the user-visible defect or the authoritative state/persistence boundary, then implement the smallest correction.
- Prove each requirement at the lowest reliable seam:
  - pure tests for algorithms, parsing, transformations, and deterministic state;
  - service/view-model tests for application behavior and state transitions;
  - SQLite integration tests only for real persistence, transaction, migration, or query behavior;
  - mounted emulator/device tests only for behavior that materially depends on MAUI rendering, OS permissions, lifecycle, sensors, background execution, or platform APIs.
- Do not duplicate the same state matrix across unit, service, and mounted tests. One lower-seam matrix plus at most one critical mounted happy path is normally sufficient.
- Preserve realistic fixtures, but do not build a new fixture framework unless multiple current tests need the same genuine responsibility.
- A harness, emulator, SDK, signing, permission, or host failure is infrastructure evidence—not a product defect. Diagnose it once and allow at most one complete rerun after correction.
- Do not let harness refinement displace the product fix. If infrastructure is unavailable and stable lower seams cover the actual risk, report the mounted evidence as unavailable rather than inventing substitute tests.
- Run focused selections first. Run the complete test suite only when a shared seam makes broader regression plausible or when required by the PR workflow.
- Delete or narrow stale tests when their claimed responsibility is redundant or no longer valid. Never change production code solely to satisfy an outdated fixture.

## Validation and Evidence

- Classify failures as:
  - current-branch product regression;
  - pre-existing or out-of-scope failure;
  - test-fixture/infrastructure failure.
- Fix current-branch regressions before declaring readiness.
- Report exact commands and passed, failed, skipped, and unavailable evidence separately. Do not call a focused selection, mocked platform test, or unavailable device workflow a full-suite or mounted pass.
- Validation must be proportionate to the changed risk. Documentation-only changes normally need content/diff checks and the repository's required CI fast path, not platform builds.
- Always run `git diff --check` before handoff.

## Pull Requests and CI

- Keep commits small and imperative. Conventional commit subjects are welcome.
- PRs must link the issue, summarize product behavior, list exact validation, and note platform-specific or unavailable evidence honestly. Include screenshots only for visible UI changes where they materially help review.
- Treat the GitHub Actions `test` check on the current PR head as the merge gate. Pending, failed, cancelled, or missing checks are not successful evidence.
- Documentation-only PRs matching the CI workflow's `paths-ignore` rules intentionally have no test check. Validate their content, references, and `git diff --check` without invoking builds or tests.
- Ordinary code PRs use the focused unit/core workflow. The manual Android build is an additional risk-based or pre-release gate, not a default PR requirement.
- For a clear infrastructure or unrelated flaky failure, rerun the unchanged workflow at most once. Do not enter repeated repair loops without a current-branch counterexample.

## Dependency and Release Work

- Keep ordinary feature issues free of opportunistic dependency upgrades.
- Perform planned framework, workload, SDK, NuGet, and GitHub Actions updates in a dedicated pre-release issue after feature work is complete.
- Upgrade only to latest stable versions that are mutually compatible with the supported .NET/MAUI and platform toolchains. Validate restore, tests, Android build, and any platform-specific seam materially affected by the update.
