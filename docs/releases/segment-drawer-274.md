# Segment drawer details (#274)

Implemented against fetched clean main `a3eee75984a7bd678495c38fd31470cc5b886586`
on `fix/segment-drawer-details-274`. Current main matched the recorded review base;
there were no intervening changes. The early implementation checkpoint is `9c57912`.
Independent review is the next gate; this is not combined candidate acceptance.

The live #274 contract, parent #269 and both drawer screenshots, #273 and Wayfarer
#505 were read. Backend source at `7ae4b2c78b93dea27e77615c46361caf6e94bfd7`
was inspected read-only. Web's viewer can generate endpoint geometry and report its
vertices; Mobile counts only the geometry it actually receives and decodes. Screenshots
are presentation evidence, not proof of identical payloads.

## Behavior and limits

The existing TripSegment/projector/view-model/XAML path now shows a concise Segment
header and one vertical string-typed Start/Via/End trail. Endpoint rows survive absent
geometry and unavailable waypoint information. Full names retain commas and suffixes;
closed-loop endpoints and equal names remain separate. Measurements stack. Selected
endpoint subtitles and redundant From/To rows are removed; overview identification,
notes actions, scrolling, selection ownership and #273 map code remain intact.

`SegmentDetailsPresentation` is a presentation result, not a transport/persistence
field. Existing anchor validation still decides whether nonempty waypoint data can
supply Via rows and a count. Invalid data yields no partial count or surviving Via
subset. Independently available endpoint text remains; missing names/identities use
Place unavailable. Route-point counts use the existing parser and include repeated
coordinates, independently of anchor validity. Neither metric certifies navigability.

DTO and offline waypoint normalization lose availability evidence. Even explicit empty
collections display Waypoint count unavailable after that path. A synthetic DTO to
existing offline waypoint codec comparison proves this limitation; no historical
recovery, parser expansion, model field, migration or backend change was introduced.
The backend source comparison was source-only, without hosted execution or contact.

## Validation

- Behavioral red: 1 failed projector test, because no-waypoint input returned an empty
  trail instead of separate full-name Start/End rows. Retained in
  `%TEMP%/segment-274-red/red.trx` and `%TEMP%/segment-274-red.log`.
- Final focused green: 39 passed, 0 failed, 0 skipped. Includes ordered Via entries
  with extra/repeated geometry vertices, closed loops/equal names, missing endpoints,
  missing/malformed/unsupported geometry and independently available geometry counts.
  Existing parser, waypoint, anchor, replacement and notes checks were reused.
- Initial green attempt exposed a fixture encoding mistake and the notes source check's
  delimiter referencing the removed parsing helper. Both fixture issues were corrected;
  the notes assertions are unchanged. This source check is not mounted notes evidence.
- Android Release Compile: 0 errors, 10 existing NU1608 package-constraint warnings.
  Evaluated CompileDependsOn includes CoreCompile and XamlC. This reused generated
  Android resources and is not a clean resource build, packaging or device pass.
- Repository APK path/hash inventory is unchanged before/after compilation; the
  isolated output directory contains no APK. Candidate artifacts were preserved.

Exact commands (from repository root):

```powershell
dotnet test tests/WayfarerMobile.Tests/WayfarerMobile.Tests.csproj -c Release -p:CollectCoverage=false --filter 'FullyQualifiedName~SegmentPresentationProjectorTests|FullyQualifiedName~FinalSegmentPresentationRegressionTests|FullyQualifiedName~SegmentAnchorResolverTests|FullyQualifiedName~TripSegmentGeometryParserTests|FullyQualifiedName~TripSegmentWaypointContractTests|FullyQualifiedName~TripSheetSegmentNotesReplacementContractTests' --logger 'trx;LogFileName=green.trx' --results-directory $env:TEMP/segment-274-final -v minimal
dotnet build src/WayfarerMobile/WayfarerMobile.csproj -f net10.0-android -c Release -t:Compile -p:BaseOutputPath=C:/Users/stef/AppData/Local/Temp/segment-274-compile/ -v minimal
git diff --check a3eee75984a7bd678495c38fd31470cc5b886586
code-guard . --changed-only --json --json-mode compact
code-guard . --base-ref a3eee75984a7bd678495c38fd31470cc5b886586 --json --json-mode compact
```

Test and compilation logs are `%TEMP%/segment-274-final.log` and
`%TEMP%/segment-274-compile.log`. No full-suite or mounted pass is claimed.

Code Guard REVIEW findings are accepted: TripSheetViewModel remains the existing
presentation/selection owner and shrinks to 1,187 counted LOC within its 1,202 allowance;
its unchanged 99-line ProcessPendingSelectionRestoreAsync coordinates existing entity
restoration and notes refresh. Splitting that owner is unrelated to this drawer fix.
The service reference remains a navigable collection of per-service sections despite
its document-size warning. No allowances, exclusions or guard configuration changed.
XAML is inapplicable to Code Guard's source guards; compilation covers its typing.

Mounted long-name wrapping, scrolling, notes and Back/Close/Edit Notes reachability
remain pending for the single combined production candidate after all fixes. #269 and
release publication remain blocked on combined acceptance and the separate #505 gates.
The applicable Android delivery workflow follows review under build authorization.
No PR, merge, workflow dispatch, signing, installation, candidate replacement,
publication, production access or provider contact occurred in this handoff.
