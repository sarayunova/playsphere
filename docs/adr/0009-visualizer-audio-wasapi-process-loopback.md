# ADR-0009 — Visualizer audio via NAudio WASAPI loopback capture

Status: **Accepted** (Phase 6 — visualizer + audible audio, supersedes ADR-0008's deferral).
Date: 2026-09-01.

> **Revision (2026-09-01, same date): device-loopback replaces process-loopback.** The original
> decision below used NAudio's per-process loopback (`WithProcessLoopback(pid,
> IncludeTargetProcessTree)`). In real-world use it produced a **static** visualizer: NAudio's
> process-loopback did not attach to libVLC's shared-mode render stream on the user's machine,
> so no captured audio ever reached the analyser (only the silence-padder's zero frames were drawn).
> The fix switches to **device-whole loopback** (`WasapiRecorderBuilder().WithLoopbackCapture()`),
> the documented, battle-tested WASAPI path that reliably captures whatever Windows mixes to the
> output endpoint — including our libVLC audio. The accepted trade-off is that it also captures any
> other audio Windows mixes to that device, not only our app's. The rest of this ADR (engine keeps
> playing audio normally, `PlaySphere.Audio` module, `IVisualizerAudioSource` seam, no engine TFM
> change, no native audio-path rebuild) is unchanged. Process-loopback is abandoned as the primary
> path; `WithLoopbackCapture()` needs no Windows build-gate (though the app targets 19041 anyway).

## Context

ADR-0008 established the visualizer's PCM tap using libVLC's `SetAudioCallbacks`. Its documented
contract is that *"the audio callbacks override any other audio output mechanism"* — so while the
tap is active, **speaker output is silenced**. That gave us a working engine-to-canvas pipeline but
with an inherent limitation: you could either hear the audio **or** watch the visualizer animate,
never both. ADR-0008 explicitly deferred "visualizer + audible audio" to a future slice.

The goal of this ADR is to deliver simultaneous audible audio **and** a real-time animated
visualizer, without taking on the native-crash risks that have repeatedly hit this codebase when
the libVLC audio/render path is rebuilt mid-playback (the `Marshal.WriteByte`
`AccessViolationException`s, the deferred-attach constraints, etc. — see AGENTS.md).

## Constraints

- The playback engine (`PlaySphere.Playback.LibVLC`) is deliberately plain `net10.0`
  (platform-neutral) so `PlaySphere.PlaybackTests` and the other plain-net10.0 test projects can
  exercise the real engine headlessly. Adding a Windows-only dependency or forcing
  `net10.0-windows` on the engine breaks that story (ADR-0008's reason for rejecting NAudio there).
- The visualizer DSP core (`PlaySphere.Visualizer`) is also plain `net10.0` (Domain + Application
  only) and must stay that way.
- The `IVisualizerAudioSource` contract (`PushSamples(ReadOnlySpan<float> samples, int sampleRate,
  int channelCount)` / `Stop()`) is the seam into the analyser; the analyser is told the format per
  batch and never assumes 44.1 kHz (PICKLE.md §34).
- This environment has no desktop UI / audio automation, so whatever native audio path we add must
  be **additive and isolated** — it must never force a TFM change on the plain-net10.0 engine or
  visualizer modules, and it must degrade gracefully when the native capabilities are unavailable.

## Decision

**Keep libVLC playing audio through its normal output path (speakers always work), and capture the
audio Windows mixes to the output endpoint via NAudio 3's WASAPI *loopback* (`WasapiRecorder`),
feeding that captured PCM into the existing `IVisualizerAudioSource` pipeline. Do not register the
libVLC audio callbacks for the visualizer at all on Windows.**

- A new **Windows-only** project, `PlaySphere.Audio` (`net10.0-windows10.0.19041.0`), isolates the
  NAudio dependency. It depends on `PlaySphere.Application` (for `IVisualizerAudioSource`) — the
  same boundary rule as every other module. The plain-`net10.0` engine and visualizer modules never
  reference it.
- `WasapiLoopbackCaptureSource : IVisualizerAudioSource` (in `PlaySphere.Audio`) builds a NAudio 3
  `WasapiRecorder` via `new WasapiRecorderBuilder().WithLoopbackCapture()` (the documented,
  reliable **device-whole** loopback, superseding the earlier per-process loopback that did not
  attach to libVLC's render stream on the user's machine) and:
  - Captures everything Windows mixes to the default output endpoint — including our libVLC audio —
    so the visualizer animates in sync with what is heard (accepted trade-off: it may also include
    any other audio mixed to that device).
  - On each `DataAvailable` packet, converts the captured `ReadOnlySpan<byte>` to interleaved
    `float` samples (handling both 32-bit IEEE float and 16-bit PCM encodings) and calls
    `IVisualizerAudioSource.PushSamples(samples, rate, channels)` — the real rate/channels come from
    `recorder.WaveFormat` (never assumed).
  - Pads silence: WASAPI loopback fires no callbacks while nothing plays; a bounded timer/`Stop`
    path synthesizes zero-filled frames so the visualizer doesn't freeze during quiet or silent
    passages.
  - `Start()` / `Stop()` lifecycle; `Stop()` clears any buffered samples (matching
    `IVisualizerAudioSource.Stop` semantics) and stops the recorder.
- **The engine keeps playing audio normally.** `IMediaPlaybackEngine.AttachVisualizerSourceAsync`/
  `DetachVisualizerSourceAsync` remain in the contract as the **non-Windows fallback** (their
  speaker-silencing limitation is unchanged, per ADR-0008), but `PlayerPage`'s visualizer toggle no
  longer calls them. Instead it starts/stops the loopback source.
- DI: a new `AddPlaySphereAudio()` extension registers `WasapiLoopbackCaptureSource` as
  `IVisualizerAudioSource` (wrapping the concrete `PcmAudioSource`), overriding the default
  registered by `AddPlaySphereVisualizer()`. If the loopback capture cannot build/start at runtime
  (`StartAsync` degrades gracefully), the source stays inactive rather than breaking the player.
- `PlayerPage` resolves `IVisualizerAudioSource` from DI as before, but now treats it as the
  loopback source on Windows: the visualizer toggle simply starts/stops the loopback capture, and
  **never** (de)attaches the engine PCM tap. The engine remains completely audio-path-agnostic here.

### Why loopback capture instead of full NAudio dual-output playback

The "dual output" idea (route libVLC PCM through NAudio `WasapiOut` for speakers + tap for the
visualizer) requires intercepting libVLC's decoded PCM (i.e. registering its audio callbacks, which
silence the native aout) and then re-*playing* that PCM through a separate NAudio output device.
That is a genuine re-architecture of the playback audio path, requires the engine to take a
`net10.0-windows` TFM (breaking the headless test story), and re-enters the exact native-path
mid-stream rebuild territory that has produced uncatchable `AccessViolationException`s in this
codebase. Loopback capture, in contrast, is purely **additive**: it reads what Windows mixes to the
output endpoint and feeds it to the visualizer. The speakers get their audio from the same
untouched libVLC path that already works. Far lower risk, no engine TFM change, no native rebuild.

### Documented limitation (accepted in this ADR)

- **Process-loopback was abandoned for device-loopback** (see the revision note at the top): the
  per-process path did not deliver our libVLC audio on the user's machine (static visualizer), so we
  now capture the **whole output endpoint** via `WithLoopbackCapture()`. This reliably captures our
  audio but also mixes in any other audio Windows sends to that device (e.g. another app playing).
  For a personal media player this is accepted; there is no clean per-stream isolation at the WASAPI
  loopback level.
- If the WASAPI render device is unavailable, `StartAsync` degrades gracefully (source stays
  inactive with only silence-pad frames) rather than breaking the player.
- Device loopback captures at the device's mix format; the `WasapiLoopbackPcmConverter` handles the
  common IEEE-float, 16-bit PCM and Extensible encodings so rate/channels are read per batch (never
  assumed).
- There is inherent loopback latency (tens of ms) versus the zero-latency PCM tap; the visualizer
  may lag the audio by a small, constant amount. The analyser/latest-frame channel already drop
  stale frames, so a small consistent lag is harmless to the animation.
- WASAPI loopback fires no callbacks while the endpoint is totally silent; the silence-padder keeps
  the visualizer alive during quiet/stopped passages but responds to (near-silent) RMS normally.

## Consequences

- `PlayerPage` no longer calls `AttachVisualizerSourceAsync`/`DetachVisualizerSourceAsync`, so the
  mid-playback-deferral complexity and its "toggle only takes effect on next file load" UX
  limitation disappear **on Windows**: the visualizer now attaches/detaches live because it no
  longer touches libVLC's audio-callback registration at all.
- The engine and visualizer modules stay plain `net10.0`; all NAudio/Windows-specific code lives in
  `PlaySphere.Audio`.
- The engine PCM-tap member set remains (fallback), keeping `PlaySphere.Playback.Abstractions` /
  `PlaySphere.Application` reference intact — no contract removals.
- A new `src/PlaySphere.Audio` project and a new package (`NAudio.Wasapi` 3.0.1) are added to the
  solution and `Directory.Packages.props` respectively.

## Alternatives considered

- **Full NAudio dual-output playback** (route libVLC PCM through NAudio `WasapiOut`): rejected here
  for the reasons above — engine TFM change, native-path rewrite, AV risk, and no headless
  verifiability. It remains a possible future path behind a separate ADR.
- **Per-process loopback** (`WithProcessLoopback(pid, IncludeTargetProcessTree)`): initially chosen
  for sync accuracy (captures only our app's audio) but **adopted then reverted** after it produced
  a static visualizer in practice (NAudio's process-loopback did not attach to libVLC's render
  stream on the user's machine). Superseded by device-whole loopback per the revision note above.
- **Staying with PCM-tap only / deferring**: rejected; the user explicitly requested simultaneous
  audio + visualizer, and loopback delivers it at acceptable risk.

## Verification

Engineering-verified: `dotnet build -c Release` clean (0 warnings/errors), full suite green,
`tools\run-app-smoke.ps1` launches. The loopback capture itself can only be exercised on a real
Windows audio stack, so the interactive manual pass is required (below). What can be verified
headlessly: format conversion (IEEE-float and 16-bit PCM byte→float) and the silence-padding logic,
via pure unit tests in `PlaySphere.UnitTests`.

Required manual pass: open a video (or audio file), enable the visualizer, confirm **both** the
audio is audible **and** the animated visualizer tracks it in real time; toggle the visualizer off
and confirm audio continues; enter/exit Fullscreen Visualizer; switch presets. Confirm that on a
Windows build the player no longer silences audio when the visualizer is on.
