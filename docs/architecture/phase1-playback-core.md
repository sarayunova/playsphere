# Phase 1 — Playback Core: Course Plan

Status: **Complete** — PS-110, PS-101, PS-102, PS-103, PS-104, PS-105, PS-106, PS-107, PS-108, PS-109 all complete and verified. Next is Phase 2 (Vibrant Player UI).
Source of truth: PICKLE.md Phase 1 / §13-§16, §53-§56.

## Objective

A stable, testable playback core behind the `IMediaPlaybackEngine` abstraction:
real **libVLC** decode, life-cycle-safe adapter, recoverable errors and a legal media
corpus — **no UI yet** (that is Phase 2).

## Gate (PICKLE.md)

- Play a controlled **MP4, MKV and MP3** through the engine (real libVLC, integration tests).
- State machine: `EMPTY → LOADING → READY → PLAYING ↔ PAUSED → STOPPED`, errors recoverable.
- Loading another file after a corrupted source must work without app restart.

## Decisions to ratify at implementation (ADRs to write)

1. **ADR-0003 — Playback engine: libVLC via LibVLCSharp 3.10.1** + `VideoLAN.LibVLC.Windows 3.0.23.1` native runtime (x64). Rationale: universal codec matrix, ongoing upstream, per PICKLE stack table. **Accepted** (see `docs/adr/0003-libvlc-engine.md`).
2. **ADR-0004 — Video output strategy**: libVLC renders into the WinUI window's HWND
   (`WindowNative.GetWindowHandle` + `MediaPlayer.Hwnd`). No pixel buffer hops in Phase 1;
   surface wiring is owned by `PlaySphere.Playback.LibVLC` (never ViewModels). **Accepted** (see `docs/adr/0004-hwnd-video-output.md`).
3. **Fixture tooling**: FFmpeg is a **dev-only** tool downloaded once to `tools/ffmpeg/`
   (static build) to generate a legal corpus. Not packaged with the app (native runtime dep
   tracking in `tools/validate-native-dependencies.ps1`).

## New projects / packages

| Change | Detail |
|---|---|
| `src/PlaySphere.Playback.LibVLC` | classlib (net10.0), references Abstractions + Domain; DI registration extension |
| `tests/PlaySphere.PlaybackTests` | xunit integration tests (real libVLC, x64) |
| Packages (CPM) | `LibVLCSharp 3.10.1`, `VideoLAN.LibVLC.Windows 3.0.23.1`, `Microsoft.Extensions.Logging.Abstractions` |
| `tools/ffmpeg/` | static FFmpeg for fixture generation only |

`PlaySphere.Playback.Abstractions` and `PlaySphere.Domain` must stay free of libVLC/UI/IO.

## Ticket order (small, per PICKLE §51)

| Ticket | Deliverable | Tests required | DoD |
|---|---|---|---|
| **PS-110** Corpus + tools | `tools/generate-media-fixtures.ps1`; fixtures in `samples/legal-media/`: MP4(H.264/AAC), MKV multi-audio, MKV multi-subtitle, MP3, FLAC, WAV, truncated, corrupted, malformed playlist | file exists + ffprobe-parseable; corrupted/truncated present | fixtures regenerable, legal, committed |
| **PS-101** Engine contract | `IMediaPlaybackEngine`, `PlaybackState` enum, event args (`StateChanged`, `MediaEnded`, `TimeChanged`, `PositionChanged`, `ErrorOccurred`), media/track records in Abstractions+Domain; DI extension stub | contract unit tests: state enum coverage, event arg shape | contract compiles, documented, no libVLC refs |
| **PS-102** libVLC adapter skeleton | `PlaySphere.Playback.LibVLC` project; `LibVlcPlaybackEngine : IMediaPlaybackEngine`; Core init/dispose (idempotent), DI registration | integration: engine constructs, state starts `Empty`, dispose safe | engine builds with native libs copied; dispose idempotent — **done** (5 lifecycle tests green) |
| **PS-103** Media load | `Load(MediaFile)` → parse metadata, resolve/verify file, expose tracks (§108), enter `Ready` | load MP4/MKV/MP3 → `Ready` + correct duration; nonexistent path → `Error` (recoverable) | load ≠ path-only identity; no UI calls — **done** (6 load tests green: MP3/MP4/MKV tracks+duration, nonexistent→Error, corrupted→recoverable, events published) |
| **PS-104** Play/pause/stop | transport control wired to libVLC media player | MP4 plays ~1s, pauses (time frozen), stops→`Stopped`; MP3 plays | events published on UI-safe thread — **done** (10 transport tests green: MP3/MP4 play, pause freeze, stop, lifecycle transitions, position updates, no-ops in invalid states, EndReached) |
| **PS-105** Seek | `Seek(TimeSpan)` + position/time events; clamp + recovery | seek forward/back within MKV & MP4; `TimeChanged` fires; seek beyond end clamps safely | timeline responsive; no crash on boundary — **done** (7 seek tests green: forward/back MP4/MKV, negative clamp, beyond-duration clamp, paused seek, empty-state no-op) |
| **PS-106** Speed | rate 0.25×–4× enforced | rate changes reflect `libvlc` rate API; clamp out-of-range | valid rate clamps; resume keeps rate — **done** (4 rate tests green) |
| **PS-107** Volume/mute | volume set/get, mute | set volume, mute toggle observable | persisted only in-memory (Phase 3 device mgmt) — **done** (5 volume/mute tests green) |
| **PS-108** Track enumeration | video/audio/subtitle track lists + selection API | MP4 reports 1+video/1+audio/0 sub; MKV multi-audio counts match ffprobe | counts verified against ffprobe output — **done** (2 track selection tests green) |
| **PS-109** Error recovery | unified error path; corrupted→`Error`→load valid file succeeds | truncated + corrupted MP4 each exercise recovery; engine reusable after each | no app restart required; all errors logged, bounded — **done** (8 error recovery tests green: corrupted file, truncated file, consecutive failures, missing file, state recovery, metadata clearing) |

## Test strategy (PICKLE §54 — never mock the feature in integration tests)

- `PlaySphere.PlaybackTests` runs against the **real** libVLC adapter and real generated fixtures.
- Timeouts on every async assertion (~30 s) so CI can't hang; forced cleanup in `Dispose`.
- Corrupted/truncated fixtures guarantee error-path testing.
- Unit tests cover pure logic (contract shape, state enum, argument clamping) without native libs.

## Security & robustness (Phase 1 scope)

- Media input treated as untrusted: canonical paths, `MediaFile` validity checks at `Load`,
  no shell command construction, bounded redacted logging.
- libVLC subprocesses/threads: engine exposes no I/O; only structured API calls.

## Verification (before claiming Phase 1 done)

```powershell
dotnet restore
dotnet build -c Release
dotnet test -c Release
tools/validate-native-dependencies.ps1     # extended: libVLC present + x64
tools/run-app-smoke.ps1                    # app still launches
```

## Risks

- **CI/native flakiness**: libVLC audio device on hosted runners; mitigate with `--no-audio`
  engine option for CI tests + explicit device selection later (Phase 3).
- **Fixture bytes**: keep generated artifacts small (< 1 MB each) and regeneration-scripted.
- **HWND video output** deferred to Phase 2 — Phase 1 verifies decode + state + logic headlessly.

## Estimated effort

~2–3 weeks of one engineer; PS-101/PS-110 first so PS-102+ depend only on stable contracts.