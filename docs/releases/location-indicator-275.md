# Location indicator synchronization (#275)

## Scope and source

Implements the [hardened #275 contract](https://github.com/stef-k/WayfarerMobile/issues/275)
under [#269](https://github.com/stef-k/WayfarerMobile/issues/269) and
[Wayfarer #505](https://github.com/stef-k/Wayfarer/issues/505).
Fetched main matched the recorded review base exactly:
`e64bdc12c0359488bee2a6f88053f8c624ea998c`. No intervening changes existed.
Branch: `fix/location-indicator-sync-275`.

The controlled Mapsui 5.1.0 defect was reproduced and corrected. Replacing reused
polygon geometry without `Modified()` left rendered paths at A while the dot moved
to B. `LocationLayerService` now invalidates each reused feature after its geometry
and style updates, before layer-change publication. This includes the reused stale
marker. New features already have independent identities; removed features remain
removed. No feature replacement framework or queued work is introduced.

Heading priority, movement smoothing/filtering/hold, cone width, accuracy scaling
and visibility, colors, stale styling, clear behavior and pulse state are unchanged.
No compass subscription, animation activation, recorded-coordinate change or viewport
workaround was added. `LocationIndicatorService` itself is unchanged.

Quick-location and Check-In inspection found calls into the same synchronous layer
update. Check-In shares the singleton and uses its own layer; existing layer-switch
feature recreation is retained. The correction introduces no dispatch or delayed
position replay and does not redesign that ownership. `UpdateAnimation` has no
production caller, and `StopAnimation` remains a no-op. This test uses heading/radius
updates, not direct pulse exercise or a claimed runtime timer.

## Behavioral evidence

`LocationIndicatorRenderingTests` links the actual production `LocationLayerService`
and `LocationIndicatorService`, using existing Mapsui/Skia packages. The obsolete
Trip-display indicator stub was removed and its constructor call updated. Existing
test-local location replicas were neither extended nor used as primary proof.

One short sequence runs at an ordinary viewport and a bounded transformed viewport.
Within each sequence, A, B and B-with-new-heading/radius share the same layer,
`MapRenderer`, `Map.RenderService` and unchanged viewport. No cache is recreated
between frames. Synthetic Web Mercator positions are 220 units apart; the white
background distinguishes painted pixels from empty space.

- Geometry: dot center, circle envelope center and cone construction origin agree
  at A and B. Cone origin is extrapolated from the inner/outer arc midpoints at
  radii 14 and 50, not inferred from its centroid or first vertex. Circle vertex
  distances verify the intended radius. Coincident world anchors share the viewport
  projection used for pixel probes.
- Pixels: distinct probes cover dot, accuracy-only and cone-only regions. The full
  sampled A footprint must be white after moving to B; no valid B shape overlaps it.
- Interleave: B retains its coordinates while GPS heading changes north to east and
  radius grows from 25 to 35. Expanded accuracy and rotated cone paint their new
  regions; the old north-facing cone region clears and A stays empty.
- Transformed observation: the same sequence uses center (95,15), resolution 0.8
  and rotation 37 degrees, versus (110,0), resolution 1 and rotation 0. Both are
  640x400, density 1. The ordinary fixed-viewport case is the regression authority.

The red checkpoint is `8fc62aa` (tests only; production uncorrected): 2 failed,
0 passed, 0 skipped. Both geometry checks and the moved-dot check passed; B's
accuracy and cone probes were white. The retained B image visibly shows both
polygons at A. This is product reproduction, not a renderer setup failure.

After correction: 10 passed, 0 failed, 0 skipped (2 rendering cases and 8 existing
Trip-initial-display cases affected by production linking). Both pixel and geometry
assertions pass. No full-suite or physical-device pass is claimed.

## Commands and retained local evidence

```powershell
$env:LOCATION_RENDER_EVIDENCE = "$env:TEMP/location-275-red"
dotnet test tests/WayfarerMobile.Tests/WayfarerMobile.Tests.csproj -c Release -p:CollectCoverage=false --filter FullyQualifiedName~LocationIndicatorRenderingTests --logger 'trx;LogFileName=red.trx' --results-directory $env:TEMP/location-275-red -v minimal

$env:LOCATION_RENDER_EVIDENCE = "$env:TEMP/location-275-green"
dotnet test tests/WayfarerMobile.Tests/WayfarerMobile.Tests.csproj -c Release -p:CollectCoverage=false --filter 'FullyQualifiedName~LocationIndicatorRenderingTests|FullyQualifiedName~TripInitialDisplayTests' --logger 'trx;LogFileName=green.trx' --results-directory $env:TEMP/location-275-green -v minimal

dotnet msbuild src/WayfarerMobile/WayfarerMobile.csproj -p:TargetFramework=net10.0-android -p:Configuration=Release -getProperty:CompileDependsOn
dotnet build src/WayfarerMobile/WayfarerMobile.csproj -f net10.0-android -c Release -t:Compile -p:BaseOutputPath=C:/Users/stef/AppData/Local/Temp/location-275-compile/ -v minimal
git diff --check e64bdc12c0359488bee2a6f88053f8c624ea998c
code-guard . --changed-only --json --json-mode compact
code-guard . --base-ref e64bdc12c0359488bee2a6f88053f8c624ea998c --json --json-mode compact
```

Red/green PNGs and TRX files remain in the named temporary directories; command logs
are `%TEMP%/location-275-red.log`, `location-275-green.log` and
`location-275-compile.log`. Diagnostics are not committed or candidate assets.

Android Release `Compile` passed with zero errors and 10 existing NU1608 warnings.
The evaluated target includes `CoreCompile` and `XamlC` and reuses generated Android
resources. This is compilation-only evidence, not a clean resource/native build or
packaging, signing, installation or device evidence. Output was isolated; no APK
was generated there and the five existing APK hashes under the app's bin directory
were unchanged before/after compilation.

## Review and remaining gates

Code Guard REVIEW: `UpdateLocationFeatures` is 88 physical lines. Accepted because
the three feature updates form one synchronous publication preparation operation;
the added invalidations belong next to each mutation. Splitting this solely for
the metric would obscure the update boundary. The service reference document's
size REVIEW is accepted because it retains navigable per-service sections.
Allowances, exclusions and policy settings are unchanged.

Stop at a clean local checkpoint for independent review. Exact-head test CI and the
Android delivery gate follow review under applicable authorization; the manual
workflow packages, debug-signs and uploads and was not dispatched here. No PR, merge,
signing, installation, candidate replacement, publication, production access,
provider contact or personal-route collection occurred.

The controlled defect does not explain every possible physical symptom. Movement,
rotation and visual synchronization remain pending for the single combined production
candidate after all fixes. Keep #269 open and release publication blocked pending
combined physical acceptance and the separate #505 publication decision.
