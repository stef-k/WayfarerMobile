# Segment straight-connection correction

Local implementation for independent review, 2026-09-06. Physical acceptance remains pending.

## Contract and identity

- Branch: `fix/segment-map-visibility`.
- Verified main/tested base: `b2c2c5c20fb570d4a94de7b2cd3ea46c52b42aac`.
- Investigation checkpoint: `8ed0ff4483021ab19966e19116fd7ac9b47845d5`, preserved in history.
- First implementation checkpoint: `d2323b1`.
- Maintainer approved the bounded exception before implementation. #272/#273 were
  updated and their complete server bodies reread and verified; both remain open.
- #274's count contract and backend implementation were not edited.

The [original investigation](../segment-visibility-contract-decision.md) retains source,
candidate checksum and screenshot evidence. The tested APK and screenshot were not
replaced. No actual payload, device database or production access occurred.

The demonstrated boundary was absent ordinary input: Web can derive a two-endpoint
line while its API returns null geometry for the zero-waypoint case. Mobile preserved
null through reconstruction, skipped the ordinary line, and derived selected cues from
anchors. This explains the synthetic combination, not the maintainer's uninspected payload.

## Resulting behavior

`SegmentDisplayGeometry` is a presentation-only eligibility helper shared by the map
and drawer. It permits a straight connection only for null/blank geometry, no custom
route flag, no available intermediate entries and valid saved endpoints accepted by
the existing anchor resolver. It never substitutes for nonempty rejected geometry.

`MapDisplayViewModel` supplies current Trip Places to ordinary line population. The
ordinary layer receives the connection before selection; selected decorations retain
that line and use the same endpoint resolution. Valid geometry still uses the existing
parser, coordinate order and mode-colored stroke. Layer order and readiness/ownership
logic are unchanged. No visibility switch was introduced.

The drawer explicitly labels the fallback **Straight endpoint connection — route geometry
unavailable**. Its two display vertices do not become a numeric route-point count;
waypoint and route-point unavailable labels remain unchanged. Empty waypoints still
cannot certify authoritative zero or completeness. No generated connection is written
to TripSegment.Geometry, persistence or navigation; provider behavior is unchanged.

Selected chevrons use Web brown `#852D10` with white casing. Their existing 10-by-10
logical-unit arms, width-4 casing/width-2 center, rotation, spacing, clearance and cap
remain unchanged. This aligns the palette without increasing the accepted envelope.

Badges use Web blue `#0057b8`, white outline and bold 12-unit sans-serif text, rasterized
at twice display density. Single labels occupy 24 by 24 logical units and combined
labels widen using Web's existing sizing formula. The image helper owns only drawing;
Mobile still owns coalescing, overlap suppression and selected/navigation lifecycle.
Measured overlap boxes use the same image dimensions. Mapsui image offsets scale with
the symbol and use the opposite Y convention from labels; the offset retains the
existing 34-unit placement above the endpoint. A rendered pixel assertion protects it.

## Validation

```powershell
dotnet test tests/WayfarerMobile.Tests/WayfarerMobile.Tests.csproj --configuration Release --filter 'FullyQualifiedName~Segment|FullyQualifiedName~TripInitialDisplayTests' --verbosity quiet
dotnet build src/WayfarerMobile/WayfarerMobile.csproj -f net10.0-android -c Release -t:Compile -p:BaseOutputPath=C:/Users/stef/AppData/Local/Temp/segment-map-visibility-compile/ -v quiet
code-guard . --changed-only --json --json-mode compact
code-guard . --base-ref b2c2c5c20fb570d4a94de7b2cd3ea46c52b42aac --json --json-mode compact
git diff b2c2c5c20fb570d4a94de7b2cd3ea46c52b42aac --check
```

- Focused tests: **132 passed, 0 failed, 0 skipped**. Existing unused-event and unused
  theory-parameter warnings remain. This is not a full-suite pass.
- The missing-line regression failed before correction and passed afterward. It exercises
  synthetic DTO/codec/reconstruction, production ordinary/selected layers, rendered
  line pixels and the drawer explanation/counts. The initial-display test additionally
  proves current Trip Places reach the displayed layer before selection.
- Rejected malformed/multipart geometry, missing/invalid endpoints, custom-route flags
  and available intermediate entries do not acquire a fallback. Existing parser,
  anchor, count, navigation, rotation, spacing and stale-ownership tests were reused.
- Palette assertions cover actual image bytes and displayed pixels. Offscreen rendering
  preloads only image sources from the synthetic layers using Mapsui's existing cache;
  it does not load unrelated globally registered images. One test-only interface-call
  compilation error during this adjustment was corrected before the final pass.
- Android `Compile`: **0 errors, 10 existing NU1608 dependency-constraint warnings**.
  Evaluated CompileDependsOn includes CoreCompile and XamlC. This reuses generated
  Android resources; it is not a clean resource build or packaging/device evidence.
  Output is isolated in the temporary directory above. No APK/signing workflow ran.
- Code Guard REVIEW is accepted for TripLayerService (cohesive layer population/styles),
  MapDisplayViewModel (existing displayed-map ownership/orchestration) and unchanged
  82-line UpdateTripAreas (linear polygon population). No policy changes are justified.
  The 1493-line Services reference also has an accepted size REVIEW: its service-index
  structure remains coherent and the change belongs in the existing Trip section.
  Existing allowances/exclusions remain intact; there are no FAIL or INCOMPLETE findings.

Offscreen ordinary/selected/bent observations are retained under
`C:/Users/stef/AppData/Local/Temp/segment-map-visibility-render/`, at density 1 with
640-by-400 viewports and existing resolutions 10/20. They are synthetic evidence, not
Android screenshots. Source-defined Web values were inspected; no new Web runtime test ran.

## Remaining acceptance

Stop for independent review. No PR, signing, installation, candidate replacement or
publication. Keep #272/#273/#269 open and publication blocked under Wayfarer #505.
Initial visibility, selected line, badge/chevron appearance and the new explanation
still need combined physical-device confirmation. Moving-location synchronization
remains untested while stationary; Trip Place directions were not separately confirmed.
Retain the maintainer's accepted dropped-pin directions, Directions selector, otherwise
correct Segment drawer and chevron-size observations without extending those claims.
