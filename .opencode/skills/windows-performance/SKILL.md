---
name: windows-performance
description: ETW, dotnet-trace, memory, startup and CPU/GPU profiling for PlaySphere.
---

# Windows Performance

## Reference targets (§44)
- Cold app launch ≤ 3s
- Local file open-to-first-frame ≤ 2s
- Timeline feedback < 100ms
- UI 60 FPS
- Library: 10,000 items without navigation lockup

These are engineering targets, not guarantees across every codec/device.

## Visualizer (Phase 6)
- 60 FPS normal / 30 FPS power-saver (`ReducedMotion`), <100ms perceived audio-to-visual response.
- Playback thread never waits for the visualizer; drop visualizer frames before audio.
- Never reduce playback audio quality to improve rendering.

## Practices
- The render loop is Win2D animation-thread; do not allocate brushes/geometry per frame at 60 FPS (only internal float smoothing/time state persists).
- Win2D animated draws must avoid per-frame `CanvasLinearGradientBrush` allocation (documented native-AV hazard).
- Profile subprocess-heavy work (ffmpeg) with `ProcessPriorityClass.BelowNormal` so heavy jobs never starve playback.
- Prioritize high-CPU/GPU hotspots in native playback/render paths before optimizing managed glue.
