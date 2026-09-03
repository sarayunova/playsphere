---
name: network-stream-diagnostics
description: Diagnosing network stream playback, buffering, reconnect and the visualizer-freeze-on-stream issue.
---

# Network Stream Diagnostics

## Feature overview
Open URL streams (HTTP/HTTPS, HLS/DASH, RTSP/RTMP, IPTV, podcasts). No DRM circumvention (§26).

## Key instrumentation
- `%LOCALAPPDATA%\LiVi Play Sphere Studio\libvlc.log` — libVLC diagnostics (subscribe at LibVLC creation time).
- `BuildFailureContext()` pulls the bounded recent-log tail into the on-screen error message.
- `PlaybackState.Buffering` chip shows stall state. `IsSeekable` disables the timeline/skips + shows a red LIVE badge on live/interminable streams (duration 0).
- Bounded auto-reconnect: ≤3 retries with 1s/2s/4s backoff; reloads the same URI onto the existing player (never a native rebuild), replay + best-effort position restore. `Play()-returned-false` on a remote stream when no libVLC error line is present also triggers it.

## Known limitation
A **silent stall** (Buffering fires, never resumes, no EncounteredError, no position advance on live radio) is NOT auto-recovered by design — a reliable detector is not achievable without native-manipulation risk. Needs a product decision.

## Manual-QA remaining (Phase 8)
Open a livestream/IPTV URL, unplug the network mid-play, confirm the Buffering chip → auto-reconnect → resume; confirm a permanently-unreachable stream ends in Error without crashing.
