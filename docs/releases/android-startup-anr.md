# Android startup ANR investigation

## Status and preservation

Wayfarer #505; 2026-09-05. Publication remains blocked. The failed candidate is
1.3.0/code 4 from `0387e8a7b8747f602ea212c3ee14e1ba350ee632`.
Fetched `origin/main` matched that source before creating `fix/android-startup-anr`.

The preserved directory is
`C:\Users\stef\releases\WayfarerMobile\1.3.0-code4-0387e8a7`.
The failed `WayfarerMobile-1.3.0-code4.apk` SHA-256 was verified as
`9f0d724cf4d983d4f5099270687c54efd45bbcbf482402729a60c92625a139d0`.
GitHub release tag `1.3.0` remains a draft targeting the frozen source; its uploaded
APK digest matches. No evidence files, release assets or release metadata were changed.

The maintainer reports Android's "failed to complete startup" ANR. The preserved
app-only `startup-anr-app-threads.txt` shows the main native thread in a Mono runtime
wait (`mono_w32handle_wait_one`, condition/futex wait). It contains no managed stack
identifying the waiting authentication caller. This is device evidence of a wait,
not confirmation of its managed cause. No new launch or device collection was performed.

## Reachable dependency and controlled reproduction

The frozen source has this startup ordering:

1. `App` applies appearance settings, starts template preload and exception handling,
   then calls `StartBackgroundServices` from its constructor on the UI thread.
2. `StartBackgroundServices` evaluates `LocationPipelineWiring.EnsureBootstrappedAsync`
   before passing its task to `SafeFireAndForget`. Async execution is initially inline.
   The pipeline claims its bootstrap guard, resolves services and calls
   `ISettingsService.PreloadSecureSettingsAsync` before starting drains or wiring delegates.
   The resolved queue/timeline/API/storage constructors do not read authentication.
3. Settings delegates to the singleton `CommittedAuthenticationAuthority.PreloadAsync`.
   With no snapshot, it acquires the semaphore and calls `LoadAsync`. An incomplete
   protected-store read captures the UI context in both load and preload continuations.
   The semaphore remains owned until preload's `finally` runs.
4. `StartBackgroundServices` continues to `ActivitySyncService.AutoSyncIfNeededAsync`.
   Before its first await, this reads `IsConfigured`, then `ServerUrl`/`ApiToken`,
   which delegate to `CommittedAuthenticationAuthority.Current`.
5. `Current` synchronously joins `Task.Run(PreloadAsync)`. That worker waits for the
   already-owned semaphore. Even after storage completes, the original owner cannot
   resume on the blocked UI thread to publish the snapshot and release the semaphore.
   Moving only the joining call to `Task.Run` does not move the original owner.

Visit notification startup also checks `IsConfigured` synchronously. App-lock
initialization follows background startup in the constructor; shell/window creation
and activation-triggered location startup follow it. They need not be reached for
the cycle above. Resume health/recovery checks also read configuration but are not
required for this cold-start reproduction. The headless Android bootstrap already
runs inside `Task.Run`; it does not itself supply the UI context in this ordering.
The pipeline guard is separate from the authentication semaphore and does not unblock it.
The Android safe synchronization-context wrapper forwards posts to its inner context.

`MauiProtectedAuthenticationStore` delegates directly to SecureStorage. The pinned
[MAUI 10.0.100 Android implementation](https://github.com/dotnet/maui/blob/10.0.100/src/Essentials/src/SecureStorage/SecureStorage.android.cs)
runs protected reads/writes through `Task.Run` using application context. Thus the
demonstrated dependency is in our awaiting continuations, not a required UI callback
inside those Android storage operations.

`AuthenticationStartupTests` links the actual authority through the existing test
project. A dedicated caller thread starts preload on a held synchronization context,
then reads `Current`. A completion source holds the envelope read until preload has
acquired the gate. Completing storage must let `Current` return the real credentials
without pumping that context. A posted continuation detects the baseline defect;
no sleeps determine ordering. A ten-second watchdog only bounds a broken test.
Cleanup releases held callbacks and joins both tasks even when the assertion fails.
Two cases cover an existing envelope with its stable partition and legacy credentials.

Both cases failed on unchanged production source at checkpoint `beb2345`, with a
continuation posted while `Current` remained running. This confirms a reachable
managed deadlock under controlled scheduling. Attribution of the physical ANR to
that exact ordering remains unconfirmed without managed device evidence or acceptance.

## Correction and boundaries

Only preload and its load helper now use `ConfigureAwait(false)` at every await,
including gate acquisition, protected reads and migration write. Snapshot publication,
revision advancement and semaphore release can finish without the caller context.
All readers still wait for the authoritative loaded result; no placeholder credentials,
timeout, retry or synchronization bypass was introduced.

Commit/clear semantics, storage keys and envelope format, migration ordering, failure
handling, account partitions and pipeline bootstrap error handling are unchanged.
Queue, pending edits, databases and offline routing behavior are unchanged. Existing
[release persistence evidence](1.3.0.md#local-validation-and-remaining-gates) is reused;
no maintainer data was accessed. No uninstall, clear-data, logout, key replacement,
production access or provider contact occurred.

## Validation and next gate

Commands and results on the correction (2026-09-05):

- Red regression: `dotnet test tests/WayfarerMobile.Tests/WayfarerMobile.Tests.csproj --configuration Release --filter FullyQualifiedName~AuthenticationStartupTests --verbosity quiet`:
  2 failed, 0 passed, 0 skipped before production correction. A no-build normal-verbosity
  run confirmed the failure was the posted-continuation assertion, not the watchdog.
- Focused authentication/startup: `dotnet test tests/WayfarerMobile.Tests/WayfarerMobile.Tests.csproj --configuration Release --filter 'FullyQualifiedName~AuthenticationStartupTests|FullyQualifiedName~CommittedAuthenticationAuthorityTests|FullyQualifiedName~AppLockServiceTests' --verbosity quiet`:
  8 passed, 0 failed, 0 skipped. No AppLockServiceTests fixture exists; the eight
  executed cases are the six authority tests and two startup regression cases.
- Related startup consumers: `dotnet test tests/WayfarerMobile.Tests/WayfarerMobile.Tests.csproj --configuration Release --no-build --filter 'FullyQualifiedName~AuthenticationStartupTests|FullyQualifiedName~CommittedAuthenticationAuthorityTests|FullyQualifiedName~QueueDrainServiceTests|FullyQualifiedName~VisitNotificationServiceTests' --verbosity quiet`:
  85 passed, 0 failed, 0 skipped. These are controlled tests, not device acceptance.
- Android: `dotnet build src/WayfarerMobile/WayfarerMobile.csproj -f net10.0-android --configuration Release --verbosity quiet`:
  passed in 1m51.70s, 0 errors, 132 warnings. Existing AndroidX NU1608 constraints
  and XAML diagnostics remain. Log: `%TEMP%\wayfarermobile-startup-anr-android-build.log`.
  This build is compilation evidence only; no replacement candidate was prepared.
- `git diff --check`: passed. `code-guard . --changed-only --json --json-mode compact`
  and final `code-guard . --base-ref 0387e8a7b8747f602ea212c3ee14e1ba350ee632 --json --json-mode compact`:
  passed without findings. The final committed branch scope includes all four files.

The authority regression is a controlled startup dependency test, not a mounted
`App`/MAUI bootstrap test. The existing test project has no direct App/pipeline
bootstrap fixture; adding a broad MAUI harness is outside this correction.

Stop for independent review, then normal exact-head CI and merge gates. Only afterward
prepare a separately identified signed candidate and record its source, version/code,
certificate and checksum without replacing the failed bytes or draft. Before any new
device launch, establish whether the freeze is at splash or a rendered page and
coordinate the attempt with the maintainer. Repeat startup and in-place upgrade
acceptance, then the short release checklist. Compilation/signing alone establish
neither startup readiness nor publication acceptance.
