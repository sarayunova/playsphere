# ADR-0005 — Thumbnails / cover art via engine-side libVLC snapshot

Status: **Accepted** (Phase 4 continuation).
Date: 2026-08-31.

## Context

`LibraryMediaItem.CoverArtPath` (PICKLE.md §23) is a cached thumbnail/cover-art path for a
cataloged item. Up to now the field was never populated. Two ways to generate a still were
on the table:

1. **Bound FFmpeg still-frame extract** (a subprocess, like FFprobe) — reuse the bounded
   subprocess pattern from `PlaySphere.Media.Probe`, but it decodes separately and needs to
   start again from scratch for each item.
2. **libVLC `video.takeSnapshot` engine-side** — capture a still from the already-decoded,
   already-rendered frame during playback.

## Decision

Use **engine-side libVLC snapshot** via a new `IMediaPlaybackEngine.TakeSnapshotAsync`
method (a thin wrapper over LibVLCSharp `MediaPlayer.TakeSnapshot`). Thumbnails are captured
**during playback** — exactly when a real, rendered frame exists — and the resulting PNG path
is persisted back to `LibraryMediaItem.CoverArtPath`.

- The UI never calls libVLC directly: `PlayerViewModel` calls `TakeSnapshotAsync` on the
  engine contract and persists the path via `IMediaLibraryRepository.SetCoverArtPathAsync`.
- A still is captured **once** per item: only when `CoverArtPath` is unset, the first time
  playback reaches ≥1 s, and only for items already in the media library.
- The capture is entirely **best-effort**: it never blocks or aborts playback, never throws
  on failure, and a missing/blank frame simply leaves `CoverArtPath` unset for a later attempt.
- Output goes to `%LOCALAPPDATA%\LiVi PlaySphere Studio\thumbs\{fingerprint}.jpg` (unpackaged
  app per ADR-0001), keyed by the item's content fingerprint so a thumbnail survives re-scan.

No thumbnail is attempted headlessly at scan time: scan-time has no rendered video surface, so
a snapshot there would be unreliable. Playback-time capture guarantees a valid frame.

## Consequences

- Reuses the live renderer (hardware-accelerated decode) with no separate decode subprocess and
  no Mediat/framework tooling changes beyond the engine.
- Adds two small contracts: `IMediaPlaybackEngine.TakeSnapshotAsync` and
  `IMediaLibraryRepository.SetCoverArtPathAsync` (the `cover_art_path` column already exists —
  no migration).
- The first play of a cataloged item does a tiny amount of extra work (one snapshot write).
- Thumbnails are not yet rendered in the UI cards — that's a follow-up; this ADR covers only
  generation + persistence of `CoverArtPath`.

## Alternatives considered

- FFmpeg still-frame subprocess at scan time (rejected: separate full decode per item, and
  headless timing is fragile).
- Scan-time libVLC snapshot with a headless vout (rejected: unreliable without a rendered
  surface).
- libVLC snapshot triggered directly from the UI (rejected: violates the UI-never-calls-libVLC
  rule; must go through `IMediaPlaybackEngine`).
