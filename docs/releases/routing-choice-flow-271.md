# Routing choice flow #271: implementation and acceptance record

Implementation is checkpointed for independent review, not release acceptance.
Parent [#269](https://github.com/stef-k/WayfarerMobile/issues/269) and the release
coordinated by [Wayfarer #505](https://github.com/stef-k/Wayfarer/issues/505) remain blocked.

## Scope and source

- Branch: `fix/routing-choice-flow-271`.
- Fetched main and recorded review base both resolved to
  `8a3b98f9de2976d0224cc41d1c576da00f49a589`; there were no intervening commits.
- Read the current repository instructions, hardened #271, #269 and all six
  screenshots, #270, merged PR #276, and Wayfarer #505 on 2026-09-06.
- Red checkpoint: `c7ff18a`; first coherent implementation checkpoint: `9b35f08`.
  The final handoff identifies the exact review HEAD. Reviewed history is preserved.
- Existing candidate artifacts, prior diagnostic outputs and reference repositories
  were preserved. No provider contact or production access was performed.

Trip Place and dropped-pin Directions use a single modal page, with a dedicated
presentation ViewModel and the existing coordinator owning selection, freshness,
requests and publication. The page wraps and scrolls within the safe area and keeps
font scaling enabled. The old Main picker mount/forwarder is removed. The group
method picker and startup handoff remain on their existing path; a caller that has
already chosen hosted routing does not receive another generic method choice.

Initial Direct/cancellation does no discovery, capability or route work. Loading
options does not calculate a route. Mode objects remain tied to the displayed catalog;
retries validate current invocation and provider capability again. A catalog change
uses the existing bounded rediscovery/reselection behavior. Loading failures, including
failure of that rediscovery, have catalog retry; calculation failures have route retry.
Explicit reload clears previous modes before requesting the catalog.

Direct and Cancel remain available while loading. Cancel remains available during
calculation. Pending work is cancelled/detached; completion cannot change the closed
surface or activate a route. The service checks cancellation and live authority
between awaited capability and route work. Generic transport cancellation is distinct
from explicit Direct. Already-issued requests cannot be undone.

Saved geometry still bypasses hosted selection. Exact retained matches offer reuse
and refresh inside Directions; a failed refresh preserves existing guidance and leaves
explicit retained reuse, Direct, retry and cancellation available. Publication and
retained persistence reuse the existing validation path. Pin External Maps remains
external, even without current location, and does not clear the pin as internal success.
Trip's separate external-maps action is unchanged. #270 startup/cleanup ownership remains.

## Executed evidence

Behavioral red before implementation:

```powershell
dotnet test tests/WayfarerMobile.Tests/WayfarerMobile.Tests.csproj -c Release --filter FullyQualifiedName~TripDirections_DoesNotUseSeparateMethodConfirmation -p:CollectCoverage=false --logger 'trx;LogFileName=271-red.trx'
```

One expected failure: the old implementation invoked the separate `Navigate by`
confirmation once. The new flow passes the same assertion.

Final focused preservation selection:

```powershell
dotnet test tests/WayfarerMobile.Tests/WayfarerMobile.Tests.csproj -c Release --filter 'FullyQualifiedName~NavigationCoordinator|FullyQualifiedName~HostedRoutingServiceTests|FullyQualifiedName~RetainedWayfarer|FullyQualifiedName~HostedRoutePublication|FullyQualifiedName~NavigationHudViewModelTests|FullyQualifiedName~WakeLockOwnershipTests|FullyQualifiedName~DialogServiceSelectionTests' -p:CollectCoverage=false --logger 'trx;LogFileName=271-final.trx'
```

142 passed, zero failed/skipped. This is a focused selection, not the full suite.
It covers initial zero-call choices, catalog/mode authority, operation-specific retry,
retry-time catalog reselection, duplicate loading, Direct/cancellation with late catalog
success/failure, cancellation during capability, retained reuse/refresh, and pin External
Maps without location. Existing startup, saved geometry, retained eligibility, authority,
group/member-target routing, HUD and wake-lock cases are reused. Tests use controlled
transports and existing production seams; no mounted harness was added.

Final Android C# and XAML compilation:

```powershell
dotnet build src/WayfarerMobile/WayfarerMobile.csproj -f net10.0-android -c Release -t:Compile -p:BaseOutputPath=C:/Users/stef/AppData/Local/Temp/navigation-271-compile/ -v minimal
```

Passed with zero errors and 10 existing NU1608 package-constraint warnings. The
installed SDK's inspected `CompileDependsOn` includes `CoreCompile` and `XamlC`;
it excludes APK packaging/signing. This incremental evidence reuses previously
generated Android resources and is not a clean resource build or APK/device pass.
The earlier #271 compile used `.tmp/navigation-271-compile`; those task-owned outputs
were archived at the final output location. No APK exists in that output directory.
Existing candidate and #270 outputs were not replaced. No Android workflow was dispatched.

Whitespace and complete committed branch checks:

```powershell
git diff --check 8a3b98f9de2976d0224cc41d1c576da00f49a589...HEAD
code-guard . --base-ref 8a3b98f9de2976d0224cc41d1c576da00f49a589 --json --json-mode compact
```

Code Guard REVIEW findings are accepted after inspecting the named bundled policies:

- MainPage code-behind (721 LOC) and MainViewModel (1,167) shrink within their
  existing 729/1,186 allowances. Changes remove old presentation and retain forwarding.
- Coordinator (552 LOC) retains route/selection/publication ownership. The hosted
  routing fixture (579 LOC) remains cohesive; its only original-file edit is `partial`
  so the new retained-surface cases reuse its established SQLite setup.
- Progressive invocation (152 lines, including its 98-line local submission handler)
  keeps catalog, mode, completion and cancellation state local to one invocation.
  Submission complexity 21 reflects explicit state/freshness/failure branches; extracting
  a new chooser controller would duplicate the coordinator's current ownership.
- Hosted request complexity 16 retains sequential contract validation and the added
  between-request cancellation/freshness checks.
- Services documentation (1,429 physical lines) remains the established navigable
  service reference, with the new content confined to navigation.

No FAIL, INCOMPLETE or configuration error is accepted. The LOC baseline, allowances,
thresholds and exclusions are unchanged. Test compiler/analyzer warnings remain
pre-existing; no infrastructure or product failure remains in the stated final checks.

## Pending independent review and combined acceptance

Independent review must assess state transitions, cancellation/detachment, retained
refresh behavior, retry authority, Main presentation wiring and responsive XAML.
Normal exact-head test CI and the applicable manual Android build gate follow that
review under their separate build authorization. The current workflow packages,
debug-signs and uploads artifacts; it is not compilation-only.

For the single combined production candidate after all fixes, record source/build
provenance and APK SHA-256. In one bounded physical acceptance session:

- Record the phone's actual font and display scaling; do not infer it from screenshots.
- Verify Trip/pin sequencing from initial Direct/Show route options/Cancel through
  loading and explicit mode selection, with no extra method/OK confirmation.
- Check default text size, the reported settings and one larger supported font setting:
  long valid mode labels, Segment explanation, actions, safe areas and scrolling.
- Verify native Back, Cancel and supported outside dismissal, including during loading
  and calculation; verify Direct while loading does not revive a late chooser/result.
- Verify #270 visible startup, truthful sheet/pin completion and location-driven guidance
  advancement. Check audio only when enabled and an announcement is due.
- Verify retained reuse/refresh and explicit External Maps, with controlled hosted evidence.

These native sequencing, dismissal and readability checks are pending. No per-issue
device build, signing, installation, draft-asset replacement, publication or data reset
was performed. This record does not unblock #269 or the separate #505 release gates.
