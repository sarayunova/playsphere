# Design note — A/V sync strategy for stereo balance (ADR-0011 precondition 3)

Status: written ahead of implementation, per ADR-0011's condition 3 ("an A/V sync strategy between
the FFmpeg-routed audio and libVLC-rendered video is designed and written down *before*
implementation starts"). This note exists so Stage 2 (the live FFmpeg→NAudio routing) has an
answer to work from instead of improvising sync handling mid-implementation.

## The problem

ADR-0011's chosen approach (Option A) keeps libVLC as the **video** decode/render owner but reroutes
**audio** through an FFmpeg filter graph (`pan`/per-channel `volume`) into a separate NAudio
`WasapiOut` device, so a continuous balance control becomes possible. That means, for the duration
balance is engaged, two independent decoders are consuming the same source file on two independent
clocks: libVLC's internal clock for video, and FFmpeg/NAudio's playback clock for audio. Nothing
inherently keeps them aligned.

## Constraints carried over from prior AGENTS.md incidents

- No native rebuild of libVLC's existing audio/video pipeline while media is loaded or playing —
  the recorded `AccessViolationException` crash history (ADR-0008, the reverted mid-session
  PCM-tap attach/detach attempt) makes any "reach into libVLC's live state" design unacceptable.
- The secondary audio path must be **additive**: it starts/stops independently of libVLC's existing
  output, exactly like ADR-0009's WASAPI loopback capture is additive to (not a replacement for)
  libVLC's speaker output.
- This dev environment has no interactive audio automation — sync correctness cannot be verified
  headlessly beyond unit-testing the pure math (offset calculation, drift detection). Real
  verification is manual QA on a Windows host, same as every other audio feature in this codebase.

## Chosen strategy: video is the timebase; audio balance path is periodically corrected against it

1. **libVLC's video position remains authoritative.** `IMediaPlaybackEngine.Position` (already
   exposed today) is the single source of truth for "where playback actually is." The FFmpeg/NAudio
   balance-audio path does not get a vote on transport position — seeking, pausing and rate changes
   are driven by the existing engine/VM exactly as today, and the balance path reacts to them.
2. **On enabling balance mid-playback:** the secondary path starts an FFmpeg process that seeks to
   the engine's *current* `Position` (via `-ss` before `-i`, consistent with the existing bounded
   `ArgumentList`-only ffmpeg invocation pattern in `PlaySphere.Media.Export`) and begins decoding
   from there into the NAudio output. There is an inherent, small (sub-second, empirically ~100-300ms
   typical for `-ss`+decode-start latency) startup offset the first time balance engages; this is
   accepted as a one-time correction rather than something to eliminate, since eliminating it would
   require frame-accurate seeking guarantees FFmpeg's `-ss` doesn't cleanly offer.
3. **Drift correction, not continuous resampling.** Rather than attempting sample-rate-adjustment
   drift compensation (complex, another source of audio artifacts), the design periodically compares
   the FFmpeg/NAudio path's own reported elapsed-time against the engine's `Position` on a bounded
   interval (proposed: every 2s). If the two diverge beyond a threshold (proposed: 150ms — chosen as
   perceptible-but-not-jarring per common A/V sync tolerance guidance), the secondary audio path is
   **restarted** at the corrected position (same as the initial-enable path: stop, reseek, restart)
   rather than time-stretched. A restart produces a brief (~100-300ms) audio gap; this is the
   accepted trade-off for correctness over seamlessness, and is expected to be rare in practice
   (steady decode rates rarely drift 150ms within a 2s window once past the initial seek).
4. **On every user-initiated seek, pause/resume, or rate change** (existing engine/VM operations),
   the balance-audio path is told to catch up the same way: stop, compute the new target position
   from the engine's updated `Position`, restart the FFmpeg decode from there. No attempt is made to
   keep the secondary path running "through" a seek — that would need the same fine-grained internal
   state synchronization this design deliberately avoids.
5. **Disabling balance (returning to Stereo/default)** simply stops the secondary FFmpeg/NAudio path
   and un-mutes/restores libVLC's normal audio output — no state to reconcile, since libVLC's audio
   was never actually stopped, only inaudible while the secondary path substituted for it (mirroring
   how ADR-0008's PCM tap silenced speakers only while attached).

## What is explicitly out of scope for the sync design

- Frame-accurate sync (sub-10ms). Not achievable with the "two independent processes on two clocks"
  architecture without much deeper engine integration than ADR-0011 is willing to risk; 150ms
  tolerance is the accepted bar.
- Sample-rate/time-stretch drift compensation. Deferred as unnecessary complexity given the
  periodic-restart correction above is expected to be rare and the gap it produces is short.
- Live crossfade between the pre-restart and post-restart audio (to hide the gap). A possible
  future refinement, not required for a correct first implementation.

## Open questions for Stage 2 implementation to resolve (not answered here)

- Exact NAudio `WasapiOut` device-selection story when the user has multiple output devices
  configured (does balance-audio always target the same device as libVLC's current output, or a
  fixed default device?).
- Whether the 2s/150ms constants above need to be tunable, or are fine as fixed values for a v1.
- UI feedback during a correction-triggered restart (a visible "resyncing" indicator vs. a silent
  gap) — a product decision, not an architecture one.

These are intentionally left for the Stage 2 ticket, once this design's overall shape has been
reviewed.
