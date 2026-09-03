---
name: media-libvlc-ffmpeg
description: Playback adapter, libVLC native lifecycle, FFmpeg/FFprobe, codecs, subprocess safety and media fixtures.
---

# Media: libVLC + FFmpeg

## Rules (active from Phase 1 / Phase 7)
- Playback goes through `IMediaPlaybackEngine` (`PlaySphere.Playback.Abstractions`). UI never calls libVLC directly.
- FFmpeg/FFprobe are invoked as subprocesses with **structured argument arrays**, never shell-string concatenation.
- All processes need timeouts and cancellation; errors must be recoverable without app restart.
- Media fixtures must be legally usable; never commit copyrighted movies/music.
- New container/codec/subtitle claims need a media fixture + test (PICKLE.md §55).