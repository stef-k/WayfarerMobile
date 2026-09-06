# Navigation startup correction (#270)

Implementation base: `7f0ddcbf0ad733b2c90f4a62a196b9a87a7ae7fb`, verified against
fetched `origin/main` on 2026-09-06 with no intervening commits. Implementation
branch: `fix/navigation-startup-270`. The live #270 contract governs this change;
#269 is the parent, #271 retains chooser layout/duplication redesign, and
Wayfarer #505 coordinates the blocked release.

## Reproduction and boundaries

Trip caller: `TripItemEditorViewModel` command → `TripSheetViewModel` callback →
Main callback → coordinator → `TripNavigationService` → map callbacks and HUD.
The earliest executable Direct contract failure was a discovery call before
Direct could be chosen. `4b4c149` retains the failing regression: expected zero
discovery calls, observed one. This establishes the discovery dependency, not
the device's complete failure mechanism.

Dropped-pin caller: context-menu command → method picker → Main callbacks →
coordinator route calculation/installation → pin removal → separate HUD startup.
Its initial Direct path did not contact discovery. `6d99d45` retains a separate
red test: an injected map callback exception leaves the route installed and
coordinator active. Source also showed pin removal before awaited startup and
silent map-not-ready returns. The exception is controlled failure evidence, not
an observed Android exception or a shared root-cause claim.

## Resulting startup contract

- The dropped-pin caller supplies its selection and current-target callbacks to
  one coordinator attempt. Trip and pin completion use the same essential
  activation step and return a boolean. Only success closes the sheet/clears the pin.
- Direct is explicitly available before discovery. After selection it uses a
  usable current origin; movement alone does not reject it. Provider-native mode
  discovery, saved geometry and exact retained-route priority are preserved.
- Action-sheet Cancel and unrecognized/dismissed results map to cancellation,
  never Direct. A missing selection page is an unavailable UI failure. The existing
  picker completes pending tasks on closure and supersession; layout is unchanged.
- Destination/account/session changes and superseding attempts are checked using
  the coordinator generation and caller target ownership. Hosted candidates are
  revalidated after awaited persistence, before route activation. Hosted origin
  matching keeps its existing canonical coordinate checks without a new tolerance.
- Rejected/cancelled attempts before replacement preserve current guidance.
  Essential activation failure clears the failed route, map, HUD and visit state;
  cleanup attempts every step and logs secondary failures without losing the primary
  error. A failed wake-lock release remains retryable and never releases Persistent.
- HUD display is committed before ancillary startup speech. Initial instructions
  are seeded from the location lifecycle and subsequent updates advance guidance.
  Audio and Navigation wake acquisition remain best effort. No second navigation
  service is started. Authentication, queues and offline schemas are unchanged.

## Validation and evidence

Fresh local tests use the production coordinator, Trip service, HUD, editor,
Trip sheet and context-menu command. Main's small forwarding methods and native
map/picker rendering are inspected source and Android compilation boundaries;
they are not mounted test evidence. A shared controlled hosted setup covers both
entry points without external contact. Both Direct cases forbid discovery,
capability and route calls. Both route types prove HUD instruction/progress updates
and map callback completion. Additional cases cover movement, dismissal, changed
targets/accounts, delayed persistence and essential cleanup with release retry.

The focused Release command is:

```powershell
dotnet test tests/WayfarerMobile.Tests/WayfarerMobile.Tests.csproj -c Release --filter 'FullyQualifiedName~NavigationCoordinator|FullyQualifiedName~TripNavigationRoutingRemovalTests|FullyQualifiedName~WakeLockOwnershipTests|FullyQualifiedName~DialogServiceSelectionTests|FullyQualifiedName~TripItemEditorNavigationTests' --logger 'trx;LogFileName=270-focused-review.trx' -p:CollectCoverage=false
```

Result: 99 passed, zero failed/skipped. This includes 40 production startup and
selection cases plus retained legacy coordinator/lifecycle tests; it is not a
full-suite pass. Original red TRX files and intermediate runs are retained under
`tests/WayfarerMobile.Tests/TestResults/270-*.trx` and are not committed.

Final Android Release C# and XAML compilation uses the inspected `Compile` target:

```powershell
dotnet build src/WayfarerMobile/WayfarerMobile.csproj -f net10.0-android -c Release -t:Compile -p:BaseOutputPath=C:/Users/stef/source/repos/WayfarerMobile/.tmp/navigation-270-build/ -v minimal
```

This passed with 10 existing package-constraint warnings and zero errors. The
installed SDK's `CompileDependsOn` includes `CoreCompile` and `XamlC`, without
packaging/signing targets. It reused Android resources generated by the earlier
Release builds; it is not a fresh APK or mounted acceptance result.

**Build-scope error:** two earlier default `build` runs used
`-p:AndroidBuildApplicationPackage=false`, expecting compilation only. Both passed
with 132 warnings and zero errors, but the SDK still appended `_CopyPackage` and
`_Sign` to its build targets. APKs, including a default debug-signed APK, were
produced in the isolated output directory. This exceeded the requested no-signing
boundary. Inspection confirmed `AndroidKeyStore` was unset (the SDK debug-key
path); no production signing configuration was supplied. Existing candidate
outputs were not replaced and no installation/publication occurred.

The generated bytes, their SHA-256 record, source-base archive and compilation
logs are preserved under `wayfarer-mobile-270-20260906` beneath the Windows temporary
directory. They are diagnostic evidence, not release assets or accepted artifacts.
The final `Compile` run was verified not to change those APK hashes. No dependency
upgrade is included.

Code Guard uses the maintainer-authorized [legacy LOC ratchet](../../.agent-tools/README.md),
generated from the reviewed base. Final branch checks must include all committed
changes, plus any uncommitted work, and `git diff --check`.

## Bounded Android observation plan — pending separate coordination

No installation or device operation is part of this implementation handoff.
After independent review and separately authorized artifact preparation/install:

1. Record reviewed source/build provenance and SHA-256 of the exact installed
   APK. Preserve failed candidates and app data; version 1.3.0/code 4 is insufficient
   to identify bytes. Record the source/build identity without secrets or device IDs.
2. In one observation session, open a Trip Place and choose Directions → Direct
   with a usable location. Confirm the sheet closes only when the route and visible
   HUD appear. Move enough to receive a location update and confirm distance or
   progress changes. Check audio only if enabled and an announcement is due.
3. Stop navigation, drop a pin and choose Directions → Direct. Confirm the pin
   clears only when visible guidance appears, then observe advancement on a location
   update. Check native Back/Cancel/outside dismissal separately keeps the pin or
   sheet and does not select Direct.
4. Record each entry point, selected mode, visible guidance and advancement result.
   If native UI does not complete or HUD fails to mount, retain that evidence and
   stop. Do not create a harness, contact a provider or loop through installations.

Direct supplies the planned mounted evidence; hosted behavior uses the controlled
production integration tests. Native rendering/closure order and physical location
advancement remain unverified until this session. The reported Android symptom is
not certified resolved by desktop tests alone. Release/publication remains blocked
pending correction review, normal exact-head CI and device acceptance.
