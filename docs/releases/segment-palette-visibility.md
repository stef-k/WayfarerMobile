# Segment palette and visibility review checkpoint (#273)

Implementation is ready for independent review, not candidate acceptance.
Parent #269 and release publication under Wayfarer #505 remain blocked.

## Contract and source

Fetched clean main matched `801fac2797f1eb8b3ccd15d26740f9eb2a2a3c58` exactly;
there were no intervening commits. Branch: `fix/segment-palette-visibility`.
First implementation checkpoint: `2c31885`.

Read repository instructions and live #273/#272. Updated only #273 using
`gh issue edit --body-file -`, appending the explicitly approved palette/visibility
amendment before implementation. It supersedes conflicting mode-color and
no-checkbox clauses while preserving the historical body and evidence. Reread the
complete server body and compared it with the intended content (ignoring trailing
whitespace). Final reread UTF-8 SHA-256:

- Raw: `185A1201AA62B5EDFD7E70ADF72B0B353691F3146ED5CD65F291D72E2CC055F6`
- LF-normalized: `DD71E1888817A49194CE946D3B8904273A32107CB5C16D3ED5F381863E087030`

Read-only Web reference remains `7ae4b2c78b93dea27e77615c46361caf6e94bfd7`.
`wwwroot/js/Trip/tripViewerHelpers.js:238` uses `#0d6efd` for ordinary resolved
Segment lines; line 370 uses `#852D10` for chevrons. Mobile now uses that blue RGB
with alpha 220 and its existing mode widths. Brown chevrons, white casing, badge
styling, dimensions, spacing, collision handling and selected ownership are unchanged.
Web comparison is source-only. Mobile offscreen renderer evidence is synthetic.

## Behavior and ownership

`MapDisplayViewModel` owns `SegmentRows`; each `SegmentMapRow` holds the drawer's
transient checkbox state and current Segment. The drawer uses this collection and
passes the underlying Segment to the existing selection command. Its checkbox is a
sibling of the selection surface, with a 48-by-48 target, native checked semantics,
an accessibility description and a readable label; it has no ancestor selection tap.

All rows start checked; geometry eligibility still decides whether anything renders.
Stable Segment IDs retain choices across same-Trip object replacement, reorder and
refresh. Removed IDs disappear; newly added IDs default checked. Unload and loading
a different Trip reset choices. Detached row callbacks cannot affect replacement rows.
Hidden selections and viewport refresh resolve through the same visible-row filter.
Restoration clears/rebuilds ordinary output and selected decorations without duplicates.
There is no separate map fallback label; the existing details explanation remains.
No visibility state enters DTOs, storage, navigation guidance or routing authority.

## Validation

Final focused result: **66 passed, 0 failed, 0 skipped**. New production-linked cases
cover hidden selection/refresh and restoration for stored geometry and the approved
straight connection, unchanged Places/navigation layers, navigation suppression,
same-Trip replacement, removal/re-addition, stale rows and unload/Trip switch.
Palette checks cover ordinary styles and the existing offscreen renderer. Existing
geometry, fallback, direction, spacing, count and lifecycle tests are reused.
An initial test compile typo in the Mapsui namespace was corrected; it was a test
authoring error. No product failures remain in the focused selection.

```powershell
$env:SEGMENT_RENDER_EVIDENCE = "$env:TEMP/segment-palette-final"
dotnet test tests/WayfarerMobile.Tests/WayfarerMobile.Tests.csproj -c Release -p:CollectCoverage=false --filter 'FullyQualifiedName~SegmentRenderingTests|FullyQualifiedName~TripSegmentGeometryParserTests|FullyQualifiedName~SegmentDecorationProjectorTests|FullyQualifiedName~SegmentAnchorResolverTests|FullyQualifiedName~FinalSegmentPresentationRegressionTests|FullyQualifiedName~TripInitialDisplayTests' --logger 'trx;LogFileName=focused.trx' --results-directory $env:TEMP/segment-palette-final -v quiet
dotnet build src/WayfarerMobile/WayfarerMobile.csproj -f net10.0-android -c Release -t:Compile -p:BaseOutputPath=C:/Users/stef/AppData/Local/Temp/segment-palette-compile/ -v minimal
git diff --check 801fac2797f1eb8b3ccd15d26740f9eb2a2a3c58
code-guard . --changed-only --json --json-mode compact
code-guard . --base-ref 801fac2797f1eb8b3ccd15d26740f9eb2a2a3c58 --json --json-mode compact
```

Android Compile passed with 0 errors and 10 existing NU1608 warnings. Evaluated
CompileDependsOn includes CoreCompile and XamlC. Generated Android resources were
reused: this is not a clean resource build, full Android build or mounted validation.
The isolated output contains no APK/AAB. No APK-producing target or workflow ran.
Logs are `%TEMP%/segment-palette-focused.log` and `segment-palette-compile.log`;
PNG/TRX evidence is in `%TEMP%/segment-palette-final`.

Whitespace and complete-branch Code Guard have no blocking findings. Accepted REVIEWs
after reading the named policies: TripLayerService (556 LOC) retains its existing map
feature/style ownership and is smaller; MapDisplayViewModel (441 LOC) keeps map
publication ownership; unchanged UpdateTripAreas (82 lines) is outside this correction;
the service reference (1,500 lines) remains navigable by service sections. No allowance,
exclusion, baseline or configuration changed.

## Remaining gates

No full-suite, CI or mounted pass is claimed. Independent review and applicable exact-head
delivery gates come next under their authorization. The final combined candidate must
confirm palette, checkbox gestures/accessibility, hidden selection and initial visibility.
Trip Place directions and moving-location acceptance remain pending. The original
maintainer payload/device symptom is not confirmed resolved by these synthetic tests.
No PR, merge, signing, installation, candidate replacement, publication, production
access or provider contact occurred; prior candidates and reviewed commits remain intact.
