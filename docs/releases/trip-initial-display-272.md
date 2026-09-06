# Trip initial display investigation (#272)

Local implementation for independent review; combined Android device acceptance remains pending.

## Authority and source

Read the hardened [#272 contract](https://github.com/stef-k/WayfarerMobile/issues/272),
[parent #269 and its Mobile/web map screenshots](https://github.com/stef-k/WayfarerMobile/issues/269),
[presentation sibling #273](https://github.com/stef-k/WayfarerMobile/issues/273), and
[Wayfarer release coordination #505](https://github.com/stef-k/Wayfarer/issues/505).
Fetched main was clean and exactly `09ab8e349f4ea549fdb91f603de5c617226d8bfa`, matching
the recorded review base with no intervening changes. Branch: `fix/trip-segment-initial-load-272`.

## Findings and correction

The reported initial-visibility symptom was **not reproduced** by ordinary feature-membership
tests. Both data-before-map and map-before-data orders already pass before the correction.
The screenshots show selected badges and detached-looking arrows without a comparable
continuous line; they cannot establish interaction ordering, identical stored data, or
ordinary-layer membership. Presentation/perceptibility remains a #273 concern. Device
refresh and viewport behavior remain unverified; this is not proof of the device symptom's cause.

The current Load path in `MyTripsViewModel` always calls `GetOfflineTripDetailsAsync`,
online and offline. `TripDownloadService` forwards to `TripContentService`, which reconstructs
downloaded Places, Areas and Segments including stored geometry. That path is unchanged.
`MainViewModel` already publishes Trip state and calls `ShowTripLayersAsync`; `TripLayerService`
parses ordinary Segment geometry, adds line features and invalidates the layer. Loading fits
Places (or the Trip bounding-box center); selecting a Segment only updates separate decorations
and refreshes. No visibility setting or extra initialization call was added.

Controlled failures were reproduced through production code:

- A readiness waiter captured the old Trip, then consumed a replacement's pending request.
- Page unload left the pending request available, allowing readiness to reload it.
- The separate Trip-sheet map-unload callback also left a pending load available.
- Delayed Place-icon completion after switching restored the old ordinary Segment features.
- Delayed completion after unloading restored the loaded indicator and could republish layers/viewport.

`MainViewModel.Trips.cs` now owns pending readiness requests, load versions, publication and
unload. The original Trip-management methods were moved into this partial for direct testing;
`MainPage` still owns the existing platform readiness gate and forwards it unchanged.
The page and Trip-sheet map-unload callback now share display invalidation. Queueing
a replacement invalidates ongoing work without touching map controls before readiness. Unload
also clears the pending request. Cancellation on disappearance retains pending data for reappearance.

`MapDisplayViewModel` stages asynchronous Place-marker work in a private writable layer and
publishes only while its version is current. Initial load and edit refresh share this path.
Clear invalidates ongoing work. The parent checks its load version and rejected layer result
before viewport, refresh and loaded-indicator writes. UI-thread ownership is retained.
The parser, ordinary styles, selection decorations and downloaded storage are unchanged.

## Behavioral evidence

Retained red checkpoint: `76495b3704d371369e38b52e7c1f144365eb1147`.
At that checkpoint the new selection reported **2 passed, 4 failed, 0 skipped**.
The readiness failures were wrong/null Trip-state assertions; delayed failures were
stale ordinary Segment membership and a restored loaded indicator. This checkpoint remains
in history without rewriting. The first corrected run passed all six cases.

The final selection adds a queued-replacement case: old icon work must be rejected even
before the replacement reaches readiness. A final unload-path audit also reproduced the
Trip-sheet callback failure, retained at `9884d4d726b80bb21148e06892dccd2a607621f7`
(1 failed). Sharing display invalidation corrects that callback as well.
Final result: **54 passed, 0 failed, 0 skipped**.
This is a focused selection, not a full-suite or mounted pass.

```powershell
dotnet test tests/WayfarerMobile.Tests/WayfarerMobile.Tests.csproj -c Release --no-restore -p:CollectCoverage=false --filter 'FullyQualifiedName~TripInitialDisplayTests|FullyQualifiedName~TripContentServiceTests|FullyQualifiedName~TripSegmentGeometryParserTests|FullyQualifiedName~TripSheetSegmentNotesReplacementContractTests|FullyQualifiedName~TripItemEditorNavigationTests|FullyQualifiedName~TripStateManagerTests' --logger 'trx;LogFileName=trip-272-reviewed-scope.trx' --results-directory C:/Users/stef/AppData/Local/Temp/trip-272-evidence -v minimal
```

The eight new cases link the actual `MainViewModel.Trips`, `MapDisplayViewModel`,
`TripStateManager` and `TripLayerService` code, using actual Mapsui writable layers and
synthetic two-point geometry plus malformed/missing geometry. They assert ordinary Segment
membership before selection and stale Trip/Place/Segment/indicator/viewport rejection.
Only surrounding presentation/navigation dependencies, the map builder (without tiles),
and package-file reads are substituted. The main view-model partial is tested directly;
the complete page, binding lifecycle, native renderer and platform readiness signals are not mounted.
Existing test-local copies are not used as the new regression proof.

Retained local TRX files are under `C:/Users/stef/AppData/Local/Temp/trip-272-evidence/`:
`trip-272-red.trx`, `trip-272-green.trx`, `trip-272-focused.trx`, `trip-272-final.trx`,
`trip-272-sheet-red.trx`, and `trip-272-reviewed-scope.trx`.
Build/compile logs are `trip-272-test-build.log` and `trip-272-compile.log` in the same Temp parent.
Initial test-linking compilation needed platform-stub name resolution and the app's existing
Mapsui/Skia package versions; those setup errors were not product regression evidence.

## Compilation and review checks

```powershell
dotnet build src/WayfarerMobile/WayfarerMobile.csproj -f net10.0-android -c Release -t:Compile -p:BaseOutputPath=C:/Users/stef/AppData/Local/Temp/trip-272-compile/ -v minimal
git diff --check 09ab8e349f4ea549fdb91f603de5c617226d8bfa...HEAD
code-guard . --base-ref 09ab8e349f4ea549fdb91f603de5c617226d8bfa --json --json-mode compact
```

Android C# and XAML compilation passed with zero errors and 10 existing NU1608 constraint
warnings. The evaluated `CompileDependsOn` includes `CoreCompile` and `XamlC`, without APK
packaging/signing. This reuses generated Android resources; it is not a clean resource build,
packaged build, or device pass. The isolated output contains no APK. Existing candidate
artifacts were not replaced, and no Android workflow was dispatched.

Working-change and complete-branch Code Guard checks accept these REVIEW findings after
reading the named LOC and Markdown policies: MainPage (687 LOC, within 729 allowance),
MainViewModel (1,066, within 1,186), MapDisplayViewModel (440), and the existing service
reference document (1,446 physical lines). The first two shrink as pending/load ownership
moves together; map publication remains with its existing owner; the service reference
retains its navigable per-service sections. No allowances, exclusions or policy changed.

## Remaining delivery gates

Stop at the clean local checkpoint for independent review. No PR, merge, signing,
installation, candidate replacement, publication, production access or provider contact
was performed. Exact-head CI and the authorized packaging/debug-signing Android workflow
are later delivery gates. #273's continuous-line and contrasting-chevron work stays separate.
Mounted initial-load confirmation belongs to the **single combined production candidate
after all fixes**. Keep #269 and release publication blocked pending combined physical-device
acceptance and the separate #505 gates. Passing these race tests does not resolve the
original device visibility report.
