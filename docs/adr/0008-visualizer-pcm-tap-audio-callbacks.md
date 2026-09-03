# ADR-0008 — Visualizer PCM tap via libVLC audio callbacks

Status: **Accepted** (Phase 6 — engine PCM wiring).
Date: 2026-08-31.

## Context

VIS-01/02/03 built a pure DSP core; VIS-04 built the Win2D renderer; VIS-05 built the Spectrum
Flow preset. The visualizer pipeline is complete from PCM-in to pixels-out **except** the
first link: getting decoded PCM from libVLC into `IVisualizerAudioSource.PushSamples`.

libVLCSharp's `MediaPlayer.SetAudioCallbacks` is the only API that exposes raw decoded PCM.
Its documented behaviour is critical:

> "The audio callbacks override any other audio output mechanism. If the callbacks are set,
> LibVLC will not output audio in any way."

This means: once audio callbacks are registered, VLC produces **zero speaker output** — the
callback becomes the sole audio output path. The trade-off inherent in this ADR:

- The visualizer needs the PCM tap (via `SetAudioCallbacks`).
- Registering the tap silently mutes speaker output for as long as the tap is active.

The **engine is deliberately a plain `net10.0` (platform-neutral) project**, so that
`PlaySphere.PlaybackTests` and `PlaySphere.UnitTests` can exercise the real engine headlessly
on any host. Adding a Windows-only speaker-output library (NAudio WASAPI, which targets
`net9.0-windows10.0.19041`+) into the engine would either inject Windows-specific audio
playback into it or force it to `net10.0-windows` (breaking the plain-net10.0 test projects).
It is also entirely unverifiable in this build environment (no display, no audio device, no
UI automation).

## Decision

**Tap PCM only, and accept the documented consequence that speaker output is silenced while the
tap is active.** No NAudio, no dual output, no TFM change.

- `IMediaPlaybackEngine` gains three members following the exact
  `AttachVideoSurfaceAsync`/`DetachVideoSurfaceAsync` pattern:
  - `bool IsVisualizerAttached { get; }`
  - `Task AttachVisualizerSourceAsync(IVisualizerAudioSource source, CancellationToken ct)`
  - `Task DetachVisualizerSourceAsync(CancellationToken ct)`
- `Playback.Abstractions` gains a project reference to `PlaySphere.Application` (which itself
  depends only on `Domain`) — so `IVisualizerAudioSource` (an Application contract) is
  referenceable from the engine interface with no dependency cycle. The engine already
  depends on Application-layer contracts in spirit; this makes it explicit.
- `LibVlcPlaybackEngine`:
  - On attach: registers `SetAudioFormatCallback` (writes `"FL32"` into the format handle,
    accepting VLC's native sample rate and channel count — never assuming 44.1 kHz, per §34)
    and `SetAudioCallbacks` (play/pause/resume/flush/drain). The play handler marshals the
    `IntPtr` PCM to a `Span<float>` and pushes it to `IVisualizerAudioSource.PushSamples`.
  - On detach: clears callbacks by rebuilding the `MediaPlayer` via the existing
    `RebuildRenderer` path (the only way to unregister libVLC audio callbacks), which restores
    normal VLC audio output. The visualizer source is unset.
- `PlayerPage` wires the toggle: enabling the visualizer resolves `IVisualizerAudioSource`
  from DI and calls `AttachVisualizerSourceAsync`; disabling calls
  `DetachVisualizerSourceAsync`.

### Documented limitation (accepted in this ADR)

While the visualizer PCM tap is active, **speaker output is silenced** (per libVLC's callback
contract). Video, timeline, subtitles, and the animated visualizer all continue to function,
but there is no audible audio. When the visualizer is turned off, normal VLC audio output
resumes. Achieving "visualizer + audible audio" simultaneously is deferred to a future slice
that adds a Windows-specific audio-output path (e.g. NAudio WASAPI or an FFmpeg audio route)
behind a new ADR — the engine stays platform-neutral until then.

This is an acceptable interim state for Phase 6: the visualizer is a *visual* feature, and the
design deliberately prefers a correct, verifiable PCM-tap wiring over an unverifiable dual-output
rework that compromises the engine's platform-neutral testing story.

## Consequences

- The engine's `RebuildRenderer` path (video swap-chain rebuild) must re-register the audio
  callbacks on any newly created `MediaPlayer` if the visualizer is attached — the same way it
  re-carries `_rate`/`_volume`/`_equalizerConfig`.
- The audio play callback fires on VLC's native thread and must not block or allocate heavily.
  `PcmAudioSource.PushSamples` is bounded (appends to a list, analyses every full window) and is
  callable from the native thread as designed.
- When the visualizer is on, VLC audio filters (equalizer, night mode, loudness normalization)
  and the audio output device are effectively mute/no-op because there is no aout. This is a
  direct consequence of the silenced-audio limitation.
- The `Playback.LibVLC`/`Playback.Abstractions` projects now depend transitively on
  `PlaySphere.Application` (for `IVisualizerAudioSource`). This preserves the boundary rule
  "UI never touches libVLC" — the engine depending on an Application contract is the intended
  direction of the modular monolith.

## Alternatives considered

- **NAudio WASAPI dual output** (original ADR draft): register callbacks + play through WASAPI.
  Rejected: Windows-only library cannot be referenced cleanly from the plain-`net10.0` engine
  without a TFM change that breaks the headless test projects, and the audio cannot be verified
  in this environment anyway.
- **Snapshot-only** (single static PCM buffer): rejected; the spec requires real-time animation.
- **Skipping the tap / leaving the canvas blank**: rejected; the engine-to-canvas pipeline is the
  core remaining Phase 6 slice.

## Verification

Engineering-verified: `dotnet build -c Release` clean (0 warnings/errors), full suite green,
`tools\run-app-smoke.ps1` launches. Actual visualizer animation over real PCM cannot be verified
headlessly (needs a rendered swap-chain + playing audio). Required manual pass: open a video,
enable the visualizer, confirm 48 animated bars track the audio (and note that speaker audio is
silenced while the visualizer is on, per the documented limitation), disable the visualizer and
confirm audio resumes.
