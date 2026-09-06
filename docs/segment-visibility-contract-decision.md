> Historical investigation preserved from checkpoint `8ed0ff4483021ab19966e19116fd7ac9b47845d5`.
> The maintainer subsequently approved the bounded exception and #272/#273 were amended
> before implementation. See [implementation and review evidence](releases/segment-map-visibility.md).
> The pending/proposal statements below describe the original decision stop.

# Segment visibility: contract decision pending

Investigation for Mobile #272/#273/#274 and parent #269, 2026-09-06.
This is a proposal, not an approved geometry contract or a device acceptance report.

## Identity and evidence

Fetched Mobile main and tested source both resolve to
`b2c2c5c20fb570d4a94de7b2cd3ea46c52b42aac`; there are no intervening changes.
Investigation branch: `fix/segment-map-visibility`.
Read-only backend reference: `7ae4b2c78b93dea27e77615c46361caf6e94bfd7`.
Live Mobile #269/#272/#273/#274 and Wayfarer #505/#577 were read.

Maintainer-reported candidate: 1.3.0/code 4, APK SHA-256
`c60fa1d431fdf2d281c8f8d04a5bc0e445f60aad18821df8f96efcbe10f87210`.
The supplied Desktop `segment.jpg` was viewed without modification. Candidate bytes
were not accessed or rebuilt. No actual payload or device database was accessed.
Names and screenshot positions were not used as synthetic fixture data.

## Earliest demonstrated boundary

| Boundary | No stored geometry, no intermediate waypoints, valid endpoint Places |
| --- | --- |
| Backend stored state | `Segment.RouteGeometry` is nullable; route provenance has separate fields. |
| Viewer | `ViewerSegmentJourneyResolver.Resolve` derives a straight anchor LineString and reports 0 waypoints / 2 route points. The Razor list passes its `RouteWkt` to the map and its counts to the drawer. |
| API | `PublicSegmentResolver.ResolveRoute` deliberately returns null geometry for zero waypoints. With nonempty valid waypoints it instead generates an anchor connection. These paths are different. |
| Download | `TripSegment.Geometry` reads `routeJson`; `TripMetadataBuilder.BuildSegments` copies it unchanged and serializes waypoints. `TripDownloadService` saves that metadata. |
| Offline reconstruction | `TripContentService` uses `OfflineSegmentWaypointMapper`; null geometry remains null. Missing/null waypoint collections and malformed offline waypoint JSON normalize to empty. |
| Ordinary line | `TripLayerService.UpdateTripSegments` skips empty geometry before creating a feature. |
| Selected decorations | Empty, non-custom geometry is passed as null to `SegmentAnchorResolver`, which derives geometry from saved Place anchors. Badges and chevrons can therefore exist without an ordinary line. |
| Drawer | `SegmentPresentationProjector` counts only parser-decoded input vertices. Empty waypoints cannot prove authoritative zero; both counts remain unavailable. |

The retained production-linked `MissingApiGeometry_AfterOfflineReconstruction_HasDecorationsButNoOrdinaryLineOrCounts`
test reproduces the Mobile combination using synthetic JSON, the waypoint codec,
offline reconstruction, actual writable layers, ordinary/selected service methods,
and the drawer projector. It verifies that geometry remains null after presentation.
The metadata builder was source-traced; the test does not claim a full download or
SQLite round trip. Existing repository/codec round-trip tests were reused separately.
Web comparison is source evidence, not a new mounted Viewer observation.

This establishes a reachable explanation, not the cause of the maintainer's exact
Segment. For this synthetic case, no ordinary feature reaches the renderer; refresh,
colors, initialization calls and layer order cannot make that missing feature visible.
Existing supported two-point train rendering still passes. Source composition puts
Tiles below Areas below Segments, then navigation overlays, selected decorations and
Places. Population targets the stored displayed-map layer references and checks the
Trip generation after asynchronous Place work. No new composition defect was proved.

## Proposed bounded amendment — requires agreement

For #272/#273, explicitly permit a **straight Segment endpoint connection** only
when `routeJson` is null/blank, `HasCustomRoute` is false, no intermediate entries are
available, and the existing anchor resolver accepts both saved endpoint Places.
This connection describes the available endpoints only; an empty collection still
does not prove that the original waypoint collection was authoritatively empty.
Do not repair rejected, malformed, unsupported or multipart geometry with this fallback.
Do not expand parsing, change valid geometry/order/discontinuities, persist the derived
line, contact providers, or use this display connection as navigation geometry.

Use that same permitted display path for the ordinary line before selection and its
selected decorations. Keep it visible by default with the existing mode-color stroke.
In #274, identify this case as **Straight endpoint connection — route geometry unavailable**
so it is not presented as an authored or provider-calculated route. Preserve existing
unavailable count labels; do not label the two derived endpoints as decoded route points.

Distinguish the following sources without inventing provenance:

- Stored/authored geometry: preserve accepted API geometry exactly. Presence alone is
  not a license to infer who authored it.
- Provider-calculated geometry: retain its existing provenance and routing authority;
  never initiate calculation to populate the map or drawer.
- Existing API-generated waypoint connections: preserve current accepted API geometry
  behavior; the current route-point count describes decoded API vertices, not necessarily
  stored database vertices.
- Proposed local endpoint connection: transient display vertices only, explicitly
  identified as straight, with no numeric derived count added.

Minimum issue changes are #272/#273 fallback eligibility and shared line/decorations,
plus the narrowly scoped #274 display explanation. #269 needs coordination only.
Application boundaries are Mobile map presentation and drawer explanation; no API,
schema, persistence, provider, navigation or backend changes are proposed. #505 remains
the release gate; #577 remains a separate Web width refinement.

If this exception is declined, the current geometry contract cannot promise a visible
line for the demonstrated empty-input case. It must remain distinct from failures to
render eligible decoded geometry. Do not introduce a visibility checkbox.

## Palette correction still pending

The requested independent Web alignment is source-grounded: Viewer chevrons use
`#852D10` (active stroke 3, opacity 1); badge rasterization uses `#0057b8` fill,
white 2-unit outline and white `700 12px sans-serif` centered text with rounded treatment.
Mobile currently uses black/white chevrons and translucent dark-blue badges with
16-unit regular text. No palette change was made before this decision stop.
The subsequent correction must retain accepted 10-by-10 chevron arms, direction,
rotation, spacing, collision handling, selected ownership and tap behavior.

## Validation and remaining acceptance

Focused command (Release):

```powershell
dotnet test tests/WayfarerMobile.Tests/WayfarerMobile.Tests.csproj --configuration Release --filter 'FullyQualifiedName~SegmentRenderingTests|FullyQualifiedName~SegmentPresentationProjectorTests|FullyQualifiedName~SegmentWaypointOfflineContractTests|FullyQualifiedName~TripSegmentGeometryParserTests|FullyQualifiedName~SegmentAnchorResolverTests|FullyQualifiedName~SegmentDecorationProjectorTests' --verbosity quiet
```

Result: 51 passed, 0 failed, 0 skipped. Existing unused-event and unused-theory-parameter
warnings remain. This is characterization plus retained lower-seam coverage, not a
behavioral fix or a full-suite/device pass. No failing desired-fallback assertion was
added because that behavior requires agreement. Android compilation is not applicable
to this tests/documentation-only checkpoint; no workflow, packaging or signing ran.

Maintainer-accepted observations remain dropped-pin directions, Directions selector,
Segment drawer otherwise correct, and acceptable chevron size. Initial Segment visibility,
the connecting line and palette remain unresolved on device. Trip Place directions were
not separately confirmed; moving-location synchronization remains untested while stationary.
Keep #272/#273/#269 open and publication blocked. No issues or release assets were edited.
