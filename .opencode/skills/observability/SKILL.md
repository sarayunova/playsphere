---
name: observability
description: Structured logging, tracing and telemetry for PlaySphere (offline-first, no silent uploads).
---

# Observability

## Ground rules
- Paper-trail exceptions that the app suppresses: log before swallowing (AGENTS rule: "suppress errors without logging" is prohibited, §57).
- `App.xaml.cs` has an `UnhandledException` handler writing `[utc] {exception}` to `%LOCALAPPDATA%\LiVi Play Sphere Studio\crash.log` (never throws).
- `LibVlcBootstrap` writes libVLC diagnostics to `libvlc.log` (subscribe at LibVLC creation time, not engine ctor) and keeps a bounded 256-line in-memory tail via `GetRecentLogLines()`.
- `PlaySphere.Audio.VisualizerAudioTrace` writes to `visualizer-audio.log` (debounced ~every 50th normal call; lifecycle events forced).
- Bounded logs only (§43); no unbounded file growth.

## Privacy
Observability is offline — never upload logs or media silently. AI telemetry must obey Local Only mode and require explicit consent.

## Status
Local file-based logging is in place. Distributed tracing / metrics collection is a P2 item.
