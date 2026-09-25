# ADR-0011 — True stereo L/R balance requires an off-engine FFmpeg audio route

Status: **Proposed / Deferred** (no implementation in this ADR — it exists to unblock a future
ticket without re-litigating the architecture question each time it comes up).
Date: 2026-09-25.

## Context

PICKLE.md's audio feature set implies a continuous stereo balance control (distinct from the
already-implemented `AudioChannelMode` full-pan cases: Stereo / MonoLeft / MonoRight /
ReverseStereo / Dolby — see the Phase 3 section of AGENTS.md). A true balance control needs a
**continuous per-channel gain coefficient** — e.g. moving a slider left attenuates the right
channel by some fraction rather than muting it outright.

This was investigated and found **not implementable via the mandated playback engine
(libVLC/LibVLCSharp, ADR-0003) as it stands today**:

- libVLC's public C API (and therefore LibVLCSharp) exposes volume, mute, audio channel/output
  selection, and audio delay — but **no `balance` function**.
- The `remap` audio filter (the mechanism `AudioChannelMode` already uses for the coarse full-pan
  cases) is a pure channel **remapper**: it can swap or duplicate whole channels, but it has no
  per-channel gain coefficient. It cannot express "attenuate the right channel to 70%" — only "use
  the left channel for both outputs" (i.e. the existing `MonoLeft` case).

So the coarse cases PICKLE.md and AGENTS.md already ship (via `AudioChannelMode`) are the full
extent of what libVLC's engine surface supports natively. A continuous balance control is
therefore **out of reach without leaving the libVLC audio pipeline**.

## Why this needs an ADR before any code

AGENTS.md rule 9 requires an ADR before "changing the playback engine, DB, UI framework, AI
provider, packaging, GPU tech or repo boundaries." Any real fix here changes how audio reaches the
speakers for a subset of playback — a playback-engine-boundary change — so implementation work
cannot start without this decision on record first.

## Options considered

### Option A — FFmpeg-based off-engine audio route (favored direction, not yet started)

Decode/filter audio through FFmpeg (already a first-class, vetted dependency in this codebase via
`PlaySphere.Media.Probe`, `PlaySphere.Media.Export`'s clip/convert/thumbnail/audio-extraction
tooling) using its `pan` or `volume`-per-channel filter graph, then play that filtered stream out
through a **separate output device path** (e.g. NAudio `WasapiOut`, following the precedent
`PlaySphere.Audio` already set for isolating Windows-only audio dependencies in ADR-0009) — while
libVLC continues to own **video** decode/render and non-audio transport state.

Trade-offs:
- Genuinely delivers continuous balance.
- Re-introduces exactly the class of risk AGENTS.md has repeatedly hit and had to walk back:
  rebuilding or rerouting the native audio path mid-playback has caused uncatchable
  `AccessViolationException`s (see the ADR-0008 PCM-tap history and the reverted mid-session
  attach/detach attempt logged in AGENTS.md). Any implementation **must** be additive/isolated the
  way ADR-0009's WASAPI loopback capture is (new audio out, not a rebuild of the existing one) —
  copying that precedent's shape is the way to keep this safe.
- Introduces a second audio output path with its own device-selection, latency and mute/volume
  state that must be kept in sync with the existing transport controls — real ongoing complexity,
  not a small patch.
- Splitting "video renders via libVLC, audio plays via FFmpeg+NAudio" needs a synchronization
  strategy (A/V sync is currently libVLC's problem alone); this is the single hardest open question
  and is not resolved by this ADR.

### Option B — Wait for/contribute upstream libVLC balance support

Rejected as a near-term plan: no timeline, not something this codebase controls, and it would still
require a libVLC/LibVLCSharp version bump plus verification once available — an ADR of its own at
that point in any case.

### Option C — Do nothing; ship only the existing coarse `AudioChannelMode` cases

This is the **current, accepted state** and remains the default until Option A (or another option)
is actually greenlit and implemented. No further action is required to keep shipping as-is.

## Decision

**Defer.** No engine change is made by this ADR. If/when stereo balance is prioritized, the
implementation path is **Option A** (FFmpeg filter graph + an isolated, additive secondary audio
output, following the `PlaySphere.Audio`/ADR-0009 pattern), on the explicit condition that:

1. It never rebuilds or reroutes libVLC's existing native audio output for other playback (video
   audio without balance controls engaged keeps working exactly as it does today).
2. It is implemented as a new isolated module (mirroring `PlaySphere.Audio`), not inline changes to
   `LibVlcPlaybackEngine`'s existing audio path.
3. An A/V sync strategy between the FFmpeg-routed audio and libVLC-rendered video is designed and
   written down *before* implementation starts, since it is the highest-risk unknown.
4. It ships behind its own regression tests and a manual-QA pass, consistent with every other audio
   feature in this codebase (per AGENTS.md's testing/manual-QA precedent).

Until then, `AudioChannelMode`'s existing five cases remain the shipped stereo/mono answer, and any
future work in this area should start from this ADR rather than re-investigating the libVLC API
surface from scratch.

## Consequences

- No code changes ship with this ADR.
- Future stereo-balance work has a documented starting point and constraint set, satisfying AGENTS.md
  rule 9 ahead of time.
- If Option A is later chosen, expect a new `PlaySphere.Audio`-adjacent module, a `Directory.Packages.props`
  entry for whatever FFmpeg-filter/NAudio pieces are needed beyond what's already referenced, and a
  dedicated A/V-sync design note before implementation.

## Verification

N/A — no implementation. This ADR is a decision record only.
