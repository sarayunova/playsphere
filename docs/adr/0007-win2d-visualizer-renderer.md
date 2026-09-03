# ADR-0007 �?" Win2D visualizer renderer in PlaySphere.UI

Status: **Accepted** (Phase 6 �?" VIS-04).
Date: 2026-08-31.

## Context

Phase 6 (PICKLE.md A 31�?"A 37) requires classic-style audio visualizers. The DSP core
(VIS-01/02/03, ADR-relevant build only) produces `AudioAnalysisFrame`s on a latest-frame
bounded channel (`ILatestAnalysisFrameProvider`) �?" pure Domain + Application types in the
`PlaySphere.Visualizer` module, which deliberately has **no Windows/UI dependency**.

VIS-04 is the Win2D rendering engine. PICKLE.md A 58 lists "visualizer GPU technology" as a
change requiring an ADR, so this record pins down (a) the GPU technology and (b) where the
renderer lives relative to the module boundaries.

The key tension: the visualizer module is intentionally kept pure (Domain + Application deps
only, no Windows), matching the `Media/Visualizer/AI modules depend on Domain + Application
only` rule in `docs/architecture/modular-monolith.md`. A Win2D renderer inherently needs WinUI
(`CanvasControl`) and thus a Windows-targeted project.

Alternatives:

1. **Put the Win2D renderer in `PlaySphere.UI`.** The UI already targets Windows and references
   WinUI 3 / Windows App SDK. The renderer consumes `ILatestAnalysisFrameProvider` (an
   Application contract from the pure module) and draws with Win2D. The UI still never touches
   libVLC/FFmpeg/OpenAI. Keeps `PlaySphere.Visualizer` as a clean, headless-tested DSP module.
2. **Put the Win2D renderer inside `PlaySphere.Visualizer`.** Adds a Windows/Win2D dependency
   to the pure module, breaking the Domain+Application-only rule and forcing the module onto a
   Windows-only TFM, which would ripple into the headless DSP tests.

## Decision

Use **option 1**: the Win2D rendering engine lives in `PlaySphere.UI`.

- **GPU technology: Win2D** (`Microsoft.Graphics.Win2D`, the WinUI 3 `CanvasControl`/`CanvasAnimatedControl`
  integration targeting `Microsoft.WindowsAppSDK`). This is the mandated visualizer GPU tech �?" an
  optional Direct3D effect layer is parked for later (PICKLE.md A 33) and is untouched now.
- **Placement: `PlaySphere.UI`**, under a new `Visualizer` feature folder. The renderer
  (`VisualizerCanvas` control + `IVisualizerRenderer`-style draw logic) depends only on:
  - `PlaySphere.Domain.Visualizer` + `PlaySphere.Application.Visualizer` (pure module contracts),
    and
  - Win2D (`CanvasControl`) for drawing.
  `PlaySphere.Visualizer` stays pure �?" no Win2D, no Windows TFM �?" so its DSP golden tests keep
  running headlessly on net10.0.
- **Data flow:** the renderer's draw callback reads the *latest* `AudioAnalysisFrame` via
  `ILatestAnalysisFrameProvider.TryReadLatest()` and draws it. Because the channel keeps only the
  newest frame, a slow renderer drops frames before audio is ever dropped (PICKLE.md A 33/A 35),
  and the playback thread never waits for the renderer.
- **First drawable:** a spectrum-bar preset (log-mapped `AudioAnalysisFrame.Spectrum` drawn as
  vertical bars) to prove the pipeline end-to-end. Full preset set (Spectrum Flow, Neon Wave,
  Orbit Spectrum, Pulse Rings, Particle Bloom, etc. �?" PICKLE.md A 32) is the later VIS-05..09
  work; preset *selection* UI is out of scope for this ADR.

## Consequences

- Keeps the pure DSP module clean and Windows-free; only the UI project gains a Win2D package
  reference and a Windows-targeted renderer.
- The UI still never calls libVLC/FFmpeg/OpenAI directly. The seam between decode and analysis
  remains `IVisualizerAudioSource` (fed later by the engine); the seam between analysis and
  render remains `ILatestAnalysisFrameProvider`.
- Win2D is a real package dependency on the UI project; any future move to a different GPU tech
  (e.g. a raw Direct3D swap-chain) is a §58 ADR change.
- The `CanvasAnimatedControl` (continuous redraw) is chosen for the spectrum renderer so the
  visualizer animates smoothly without a manual timer; power-saver / reduced-motion / 30 FPS
  handling (PICKLE.md A 36) is follow-on polish, not part of this ADR.

## Alternatives considered

- Putting the renderer in `PlaySphere.Visualizer` (rejected: breaks the Domain+Application-only
  module rule and would push the pure module onto a Windows-only TFM, complicating its headless
  golden tests).
- A raw Direct3D 11 swap-chain renderer instead of Win2D (rejected for now: Win2D is the planned
  tech and far less code for the same result; the Direct3D effect layer noted in A 33 can be
  layered under Win2D later if ever needed).

## Verification

Engineering-verified: solution builds clean (0 warnings/errors) with the Win2D package wired
into the UI project; full DSP/playback suite green; `tools\run-app-smoke.ps1` launches. Actual
on-screen animated rendering cannot be verified headlessly in this environment �?" a manual
click-through (enable a visualizer, confirm bars/animation over playing audio) is required before
calling VIS-04 closed end-to-end.
