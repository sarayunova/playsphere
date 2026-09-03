# ADR-0003 — LibVLC via LibVLCSharp as the playback engine

Status: **Accepted** (Phase 1).
Date: 2026-08-30.

## Decision

- Playback is provided by **LibVLC 3.0 (VideoLAN.LibVLC.Windows 3.0.23.1)** through
  the **LibVLCSharp 3.10.1** managed bindings, isolated in a dedicated adapter project
  `PlaySphere.Playback.LibVLC`.
- The rest of the app (UI, domain, sessions) depends only on
  `PlaySphere.Playback.Abstractions` (`IMediaPlaybackEngine`). UI never touches libVLC.
- Native layout uses the `VideoLAN.LibVLC.Windows` package, which places
  `libvlc\win-{arch}\` + `plugins\` folders in the output. Because that package only
  copies its natives to projects that reference it **directly**, both the adapter
  project and the top-level startup project (`PlaySphere.App`, `PlaySphere.PlaybackTests`)
  reference it. This is the documented LibVLCSharp deployment pattern.
- `LibVlcBootstrap.EnsureNativeInitialized()` runs the one-time `Core.Initialize()`
  with a recursive fallback search under the app base directory; `CreateLibVlc` builds
  the `LibVLC` instance from `LibVlcOptions` (defaults suitable for headless CI:
  `--no-audio`, `--no-video-title-show`, `--no-osd`, `--verbose=n`).

## Consequences

- Proven codecs (MP4/H.264/AAC, MKV, MP3, FLAC…) and the exact VLC state/error model for
  free; we are not writing a decoder.
- LGPL obligations: libVLC is loaded dynamically and the app must not statically link
  any GPL codec; keep the engine strictly an adapter over the LGPL library.
- The `Core.Initialize` fallback directory search is capped at the app base directory to
  avoid drive-wide scanning (PICKLE.md rule 8).
- Dev/test machines need no separate install: natives travel with the output folder.

## Alternatives considered

- FFmpeg (FFmpeg.AutoGen) managed pinvoke: no player state machine, no audio output
  pipeline, far more required work for a player (PICKLE.md §13 favours libVLC).
- `MediaPlayerElement`/`MediaPlayer` (Media Foundation): no MKV/FLAC guarantees, weaker
  playback control, and a different native dependency story.
- System Media Foundation via WinRT: rejected for the same codec and control reasons.