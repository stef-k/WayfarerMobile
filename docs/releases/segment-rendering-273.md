# Segment rendering investigation (#273)

Local implementation for independent review. The original Android visibility symptom
is **unreproduced, unexplained and still unresolved**. Combined physical initial-load
and visual acceptance remain pending under #269 and Wayfarer #505.

## Authority and base

Read the live [#273 contract](https://github.com/stef-k/WayfarerMobile/issues/273),
[parent #269 and its screenshots](https://github.com/stef-k/WayfarerMobile/issues/269),
[#272](https://github.com/stef-k/WayfarerMobile/issues/272),
[PR #278](https://github.com/stef-k/WayfarerMobile/pull/278),
[release #505](https://github.com/stef-k/Wayfarer/issues/505) and
[Web #577](https://github.com/stef-k/Wayfarer/issues/577).
Fetched clean main exactly matched recorded base `5f4b6e02ffe37ee7c4c014713914134dfc34eb73`;
there were no intervening changes. Branch: `fix/segment-line-chevrons-273`.
Read-only Web source remains at `7ae4b2c78b93dea27e77615c46361caf6e94bfd7`.
Its three-point tangent/normal chevrons are a source-only comparison; no Web runtime
was exercised. Mobile does not depend on #577's perpendicular-width enhancement.

## Ordinary rendering before changes

Baseline checkpoint: `7312b4b5481f5d503334f0faa3005964a2797f1e`.
The test links production `TripLayerService.UpdateTripSegments`, uses actual Mapsui
`WritableLayer` instances with `Style = null`, and calls installed Mapsui 5.1.0
`MapRenderer.RenderToBitmapStream` with SkiaSharp 3.119.2 on Windows.
It adds no tiles, provider calls, personal data or copied layer implementation.

Synthetic GeoJSON is `[[1,1],[1.04,1]]` (longitude, latitude), a nondegenerate train
LineString. Projected coordinates are `(111319.490793,111325.142866)` and
`(115772.270425,111325.142866)`. Viewport: 640 by 400 logical units, center at
longitude 1.02 / latitude 1, rotation 0, pixel density 1 (one pixel per logical unit).

| Resolution | Endpoint screen X (Y=200) | Visible length | Baseline cues |
| --- | --- | --- | --- |
| 10 | 97.361 to 542.639 | 445.278 | 5 |
| 20 | 208.681 to 431.319 | 222.639 | 2 |

Ordinary pixels show an unbroken purple line at both resolutions before selection.
Style: RGB 156/39/176, alpha 220, solid width 4, round joins/caps; feature-style and
layer opacity both 1. No custom resolution cutoff. Selected output adds filled
triangles over the same visible line. This disproves neither the device report nor
a viewport/refresh/data discrepancy; it provides no missing-initialization diagnosis.
#278's separate stale-publication correction and unresolved #272 evidence remain linked.

Offscreen ordering is ordinary Segments, selected chevrons, selected badges.
Production ordering remains Areas, Segments, completed navigation, active navigation,
chevrons, badges, Place selection, Places, dropped pin, location. Places/taps are not
mounted in these images; ordering and unchanged tap ownership were inspected in source.

## Presentation correction

All mode strokes are solid; mode aliases, colors, alpha, width and gray fallback remain
unchanged. No dash accent or additional ordinary layer is needed. Parser eligibility,
coordinate order, malformed/unsupported handling and discontinuities are unchanged.

Selected open chevrons have a 10 by 10 logical-unit nominal envelope, arm length
sqrt(125) = 11.181, opaque black width-4 round casing and opaque white width-2 center.
Including stroke, the horizontal envelope is 14 by 14; the largest axis under arbitrary
rotation is at most sqrt(125) + 4 = 15.181, below 24. Black/white contrast does not
depend on mode hue. Final train images and pixel checks cover white and #202020 map
background regions at resolutions 10 and 20, density 1. These are synthetic solid
backgrounds, not a claim of acceptance over every native basemap or device display scale.

Tips lie on the resolved local tangent. A bent route and its reversed coordinate/anchor
order, rotated 37 degrees, pass attachment, direction, envelope and spacing checks.
The midpoint is a real synthetic waypoint; cues overlapping its marker envelope are
suppressed. Screen-to-world conversion lets the renderer apply map rotation once.
Viewport decoration refresh runs before the existing zoom-label throttle; this prevents
old world-projected arms from growing during zoom. It is not another initial-load call.

The placer keeps eight maximum cues and 72-unit pairwise spacing. Its 24-unit endpoint
inset gains a conservative rendered radius sqrt(125)+2 = 13.181, leaving low-length
suppression in place (production total length below 146.361 is suppressed). Cue bounds
include the 2-unit casing radius and avoid each Place's X +/-24, Y -48/+24 envelope;
this covers the 28 by 45 icon scaled 1.1 with Y offset -16 and folded-route endpoints.
No route geometry is moved. Existing badge labels, canonical coalescing, measured
overlap handling, selection ownership and navigation suppression remain with their owners.

## Validation and retained evidence

Red checkpoint: `ba43019ac7401c06e82cf1fc439e1e3a3b0f127d`:
2 passed, 5 failed, 0 skipped. Failures were three dashed-only styles and two Point
triangles where open line geometry is required. First green: 7 passed, 0 failed/skipped.
Initial test setup had namespace collisions with existing test-local types; moving
the new tests to a distinct namespace resolved compilation before baseline observation.

The focused selection reuses existing malformed/missing geometry, parser, anchor,
folded-route spacing, badge and Trip ownership tests. The test project references the
app's exact Mapsui renderer version; Linux HarfBuzz native assets match its managed
8.3.1.3 dependency for the repository's Ubuntu test gate. This is test-only dependency
alignment, not an application dependency upgrade. Linux execution remains a later CI gate.

Final focused result: **51 passed, 0 failed, 0 skipped**. This is neither a full-suite
nor mounted pass. Android C#/XAML compilation passed with **0 errors and 10 existing
NU1608 warnings**. Whitespace and complete-branch Code Guard checks passed without
FAIL, INCOMPLETE or configuration errors. Accepted REVIEW findings after reading the
named LOC, callable-size and Markdown policies:

- MainPage: 688 LOC within its unchanged 729 allowance; viewport refresh stays with
  the existing event owner and no new lifecycle infrastructure is introduced.
- TripLayerService: 580 LOC; trip feature/style ownership remains cohesive. Chevron
  projection is extracted into a named private operation to separate it from badges.
- Unchanged `UpdateTripAreas`: 82 physical lines; linear polygon publication remains
  outside this correction's scope.
- Service reference: 1,460 physical lines; navigable per-service sections remain intact.

No allowances, exclusions, baselines or policy settings changed.

```powershell
$env:SEGMENT_RENDER_EVIDENCE = "$env:TEMP/segment-273-final"
dotnet test tests/WayfarerMobile.Tests/WayfarerMobile.Tests.csproj -c Release -p:CollectCoverage=false --filter 'FullyQualifiedName~SegmentRenderingTests|FullyQualifiedName~TripSegmentGeometryParserTests|FullyQualifiedName~SegmentDecorationProjectorTests|FullyQualifiedName~SegmentAnchorResolverTests|FullyQualifiedName~FinalSegmentPresentationRegressionTests|FullyQualifiedName~TripInitialDisplayTests' --logger 'trx;LogFileName=focused.trx' --results-directory $env:TEMP/segment-273-final -v minimal
dotnet build src/WayfarerMobile/WayfarerMobile.csproj -f net10.0-android -c Release -t:Compile -p:BaseOutputPath=C:/Users/stef/AppData/Local/Temp/segment-273-compile/ -v minimal
git diff --check 5f4b6e02ffe37ee7c4c014713914134dfc34eb73
code-guard . --base-ref 5f4b6e02ffe37ee7c4c014713914134dfc34eb73 --json --json-mode compact
```

Baseline PNG/TRX files remain in `%TEMP%/segment-273-baseline`; red TRX in
`segment-273-red`; first-green PNG/TRX in `segment-273-green`; final PNG/TRX in
`segment-273-final`. Logs are `%TEMP%/segment-273-focused.log` and
`%TEMP%/segment-273-compile.log`. These diagnostics are not committed or release assets.

Android `Compile` includes C# `CoreCompile` and XAML `XamlC`, reusing generated Android
resources. It is not a clean resource build, packaging, signing or native/device pass.
Output is isolated from candidate artifacts; no APK is generated. The Android delivery
gate follows independent review under build authorization. No PR, merge, workflow
dispatch, signing, installation, candidate replacement, publication, production access
or provider contact is part of this handoff.
