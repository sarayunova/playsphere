# LiVi PlaySphere Studio — PICKLE.md
## Authoritative Build Blueprint for Pickle / Agentic Development

> **Product:** LiVi PlaySphere Studio™  
> **Developer:** LiVi Software Company  
> **Platform:** Windows 10/11  
> **Primary development model:** Pickle-style PRD-driven autonomous engineering loop  
> **Core principle:** Build a fast, polished, modern universal media player first; add AI and creator utilities only after playback quality is stable.

---

# 1. Product Mission

Build **LiVi PlaySphere Studio**, a modern Windows media player that combines broad local media playback, a fast and polished Windows-native experience, vibrant modern visual design, advanced subtitles and audio controls, media library and playlists, timeline/chapters/bookmarks, classic music visualizer experiences, conversion and clip export, optional AI transcription and media understanding, and strong privacy/offline-first behavior.

PlaySphere must feel like a **new-generation Windows media application**, not a clone of VLC, Windows Media Player, DaVinci Resolve, or any other third-party application.

The interface must be independently designed.

---

# 2. Product Positioning

## Product name

**LiVi PlaySphere Studio™**

### Tagline

> **Play Everything. See More. Understand Your Media.**

### Product statement

LiVi PlaySphere Studio is a universal Windows media player and intelligent media workspace developed by **LiVi Software Company**.

---

# 3. Non-Negotiable Product Principles

1. **Playback comes first.**
   - Local playback must never depend on AI, cloud services, or an internet connection.
   - Opening ordinary video/audio must be fast and reliable.

2. **Modern and vibrant UI.**
   - Do not use a DaVinci Resolve-style interface.
   - Do not mimic VLC.
   - Do not copy old Windows Media Player UI.
   - The product must have its own visual language.

3. **Professional quality without visual heaviness.**
   - Use a modern Windows layout with clean cards, gradients, strong typography and intentional motion.
   - Avoid excessive panels, tiny controls and dense NLE-style chrome.

4. **Original visual design.**
   - Never copy copyrighted icons, shaders, logos, themes or layouts from third-party applications.

5. **Offline-first.**
   - Video/audio playback, library, playlists, subtitles, visualizers and local tools work offline.

6. **AI is optional.**
   - AI features are disabled unless explicitly enabled.
   - AI failures must not interfere with playback.

7. **Original media is immutable by default.**
   - Editing settings, bookmarks, subtitles and AI artifacts are stored separately.
   - Source files are modified only during explicit export.

8. **Every risky operation is cancellable.**

9. **All untrusted media input is treated defensively.**

10. **No feature is considered complete without tests.**

---

# 4. Target Technology Stack

Unless changed through an approved Architecture Decision Record (ADR), use:

| Layer | Technology |
|---|---|
| Language | C# 14 |
| Runtime | .NET 10 LTS |
| Windows UI | WinUI 3 / Windows App SDK |
| Architecture | MVVM + modular monolith |
| Playback | libVLC behind abstraction |
| Media processing | FFmpeg + FFprobe |
| Local database | SQLite + EF Core |
| Search | SQLite FTS5 |
| Visualization | Win2D, with Direct3D extension point |
| Dependency injection | Microsoft.Extensions.DependencyInjection |
| Logging | Microsoft.Extensions.Logging |
| Packaging | MSIX first; optional signed EXE/MSI bootstrapper |
| AI | OpenAI API via dedicated service boundary |
| Testing | xUnit + integration tests + Windows UI Automation |
| CI | GitHub Actions or equivalent Windows CI |

---

# 5. Repository Structure

```text
playsphere/
│
├─ PICKLE.md
├─ AGENTS.md
├─ README.md
├─ Directory.Build.props
├─ global.json
│
├─ docs/
│  ├─ architecture/
│  ├─ adr/
│  ├─ product/
│  ├─ ui/
│  ├─ security/
│  ├─ testing/
│  └─ release/
│
├─ src/
│  ├─ PlaySphere.App/
│  ├─ PlaySphere.UI/
│  ├─ PlaySphere.Domain/
│  ├─ PlaySphere.Application/
│  ├─ PlaySphere.Playback.Abstractions/
│  ├─ PlaySphere.Playback.LibVLC/
│  ├─ PlaySphere.Media.Probe/
│  ├─ PlaySphere.Media.Processing/
│  ├─ PlaySphere.Library/
│  ├─ PlaySphere.Playlists/
│  ├─ PlaySphere.Subtitles/
│  ├─ PlaySphere.Audio/
│  ├─ PlaySphere.Visualizer/
│  ├─ PlaySphere.AI/
│  ├─ PlaySphere.Infrastructure/
│  └─ PlaySphere.Worker/
│
├─ tests/
│  ├─ PlaySphere.UnitTests/
│  ├─ PlaySphere.IntegrationTests/
│  ├─ PlaySphere.PlaybackTests/
│  ├─ PlaySphere.MediaTests/
│  ├─ PlaySphere.VisualizerTests/
│  ├─ PlaySphere.SecurityTests/
│  └─ PlaySphere.UITests/
│
├─ samples/
│  ├─ legal-media/
│  └─ synthetic-audio/
│
└─ tools/
   ├─ bootstrap.ps1
   ├─ verify-environment.ps1
   ├─ build-release.ps1
   └─ validate-native-dependencies.ps1
```

---

# 6. Modern Vibrant UI Direction

The PlaySphere UI should feel like a premium modern Windows application: expressive, vibrant, clean, spacious, media-focused, polished and fast.

Avoid the dense four-pane editing-workstation style.

The application should feel closer to a modern entertainment/media hub than a professional NLE.

---

# 7. Visual Design System

## Default theme: Vibrant Dark

Recommended conceptual color tokens:

```text
Background.Primary     #0E1117
Background.Secondary   #151A23
Surface.Card           #1D2430
Surface.Hover          #263142

Accent.Blue            #36A9FF
Accent.Purple          #8B5CF6
Accent.Magenta         #EC4899
Accent.Cyan            #22D3EE
Accent.Green           #32D583

Text.Primary           #F8FAFC
Text.Secondary         #B7C0CC
Text.Muted             #778394

Warning                #F6B84A
Error                  #FF5F6D
Success                #35D07F
```

Primary accent gradient:

```text
#36A9FF → #8B5CF6 → #EC4899
```

Use gradients for active playback glow, selected navigation, progress, music visualizer accents, hero cards and AI features.

Do not use gradients behind long-form text.

---

# 8. Main Application Layout

## Home

```text
┌──────────────────────────────────────────────────────────────┐
│ PlaySphere   Search media...                   Profile ⚙     │
├────────────┬─────────────────────────────────────────────────┤
│ Home       │ Continue Watching                               │
│ Library    │ [Movie Card] [Movie Card] [Movie Card]         │
│ Music      │                                                 │
│ Playlists  │ Recently Played                                │
│ Streams    │ [Cards / horizontal rows]                      │
│ AI Media   │                                                 │
│            │ Your Music                                     │
│            │ [Album Cards]                                  │
└────────────┴─────────────────────────────────────────────────┘
```

Use large cover art, thumbnail cards, rounded 8–12 px corners, hover lift and restrained blur/glass only where readable.

---

# 9. Player Screen

```text
┌─────────────────────────────────────────────────────────────┐
│ ← Library                               Media title     ⋯   │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│                        VIDEO                                │
│                                                             │
├─────────────────────────────────────────────────────────────┤
│        thumbnail preview / chapters / bookmarks            │
│ ────────────────●──────────────────────────────────────     │
│  00:42:13                                  01:51:30         │
│                                                             │
│  ⏮   -10   ▶/❚❚   +10   ⏭      🔊      CC   ⚙   ⛶        │
└─────────────────────────────────────────────────────────────┘
```

Secondary controls should appear through drawers or sheets rather than permanently consuming screen space.

---

# 10. Player Side Drawers

Use modern slide-out drawers for:

- Audio tracks
- Subtitle tracks
- Subtitle styling
- Playback speed
- Video adjustments
- Chapters
- Bookmarks
- Media information
- AI transcript
- Visualizer selection

The application must not keep a permanent technical inspector visible.

---

# 11. Compact / Mini Player

Support always-on-top compact playback, corner snapping, resize, video mini-player and audio album artwork.

---

# 12. Cinema Mode

Cinema mode:

- fullscreen video,
- hidden controls,
- transient OSD,
- controls appear on mouse movement,
- fade after configurable delay,
- no permanent panels.

---

# 13. Core Playback Requirements

Must implement:

- open local file,
- drag-and-drop,
- Windows Open With,
- command-line file opening,
- play,
- pause,
- stop,
- seek,
- skip ±5/10/30/60 sec,
- frame forward,
- frame backward where supported,
- playback speed 0.25×–4×,
- volume,
- mute,
- fullscreen,
- always-on-top,
- picture-in-picture,
- resume playback,
- chapter navigation,
- multiple audio tracks,
- multiple subtitle tracks,
- embedded subtitles,
- external subtitles,
- subtitle delay,
- audio delay,
- video aspect ratio,
- crop,
- zoom,
- rotation.

---

# 14. Media Format Targets

Target containers:

- MP4 / M4V
- MKV
- AVI
- MOV
- MPEG
- TS / M2TS
- WebM
- FLV
- OGV
- 3GP

Target audio:

- MP3
- AAC / M4A
- FLAC
- WAV
- OGG/Vorbis
- Opus
- ALAC
- WMA where supported

Target subtitles:

- SRT
- ASS / SSA
- WebVTT
- SUB/IDX
- embedded subtitle streams

---

# 15. Playback Abstraction

UI must never call libVLC directly.

Define `IMediaPlaybackEngine` with load, play, pause, stop, seek, rate, volume, mute, track selection, sync delay and observable state/error events.

---

# 16. Playback State Machine

```text
EMPTY
  ↓
LOADING
  ↓
READY
  ↓
PLAYING ↔ PAUSED
  ↓
STOPPED
```

Error must be recoverable.

Loading another file after a corrupted source must work without restarting the app.

---

# 17. Subtitle Engine

Implement embedded/external subtitles, automatic filename matching, preferred language, styling, subtitle delay, cue search, cue list and subtitle export.

> Status: styling is DONE — font/color/outline/position now join text scale via libVLC per-media
> freetype options on the existing engine boundary (see ADR-0006). Preferred-language auto-selection
> is also DONE (see AGENTS.md). Still deferred: stereo balance (not implementable via libVLC, see
> AGENTS.md). Manual on-screen QA of styling remains before calling end-to-end closed.

AI-generated subtitles must remain separate tracks until explicitly exported.

---

# 18. Audio Features

Implement:

- volume,
- balance,
- audio output device,
- mute,
- preferred language,
- track selection,
- A/V delay,
- equalizer,
- preamp,
- presets,
- loudness normalization,
- night mode,
- stereo/mono downmix where supported.

---

# 19. Video Controls

Implement:

- brightness,
- contrast,
- saturation,
- gamma,
- hue,
- rotation,
- zoom,
- pan,
- aspect ratio,
- crop,
- fit,
- fill,
- pixel-perfect mode.

All playback adjustments must be reversible.

---

# 20. Timeline

Implement:

- seek bar,
- buffered state,
- current position,
- duration,
- hover thumbnail,
- chapter markers,
- bookmark markers,
- subtitle markers,
- loop region,
- Mark In,
- Mark Out.

Timeline must remain smooth for multi-hour files.

---

# 21. Bookmark System

Store:

```text
media_id
position_ms
label
note
category
created_at
```

Clicking a bookmark seeks to timestamp.

---

# 22. Media Library

Library categories:

- Home
- Videos
- Music
- Recently Played
- Continue Watching
- Favorites
- Playlists
- Folders

User explicitly selects library folders.

Never automatically scan all drives.

---

# 23. Library Metadata

Store:

```text
file path
fingerprint
title
duration
resolution
codec
frame rate
bitrate
audio tracks
subtitle tracks
album
artist
cover art
last played
last position
watched percentage
favorite
```

Use FFprobe for technical metadata.

---

# 24. Media File Identity

Do not use path alone.

Fingerprint should combine file length, modified timestamp and sampled cryptographic hash.

Optional full hash for verification.

---

# 25. Playlist System

Implement create, delete, rename, reorder, queue, shuffle, repeat one, repeat all, play next, save playlist and M3U/M3U8 import.

---

# 26. Network Streaming

Implement HTTP, HTTPS, HLS, DASH, RTSP, RTP and UDP where supported.

Never implement DRM circumvention.

---

# 27. Media Inspector

Provide a modern bottom sheet or side drawer containing General, Video, Audio, Tracks and Playback details including codec, resolution, frame rate, bitrate, HDR/color metadata, decoder, GPU acceleration and dropped frames where available.

---

# 28. Screenshot System

Support current-frame capture, PNG/JPEG, copy to clipboard, configurable folder and filename templates.

---

# 29. Media Conversion

Use FFmpeg.

Support remux, video transcode, video-to-audio, resize, compress, clip export and audio conversion.

Never construct commands through raw shell-string concatenation.

Use structured argument arrays.

---

# 30. Background Conversion Queue

Each conversion job must support progress, cancel, retry and open-output-folder.

Do not allow heavy conversion to starve playback.

---

# 31. Classic-Style Audio Visualizer

PlaySphere must include optional music visualizers inspired by the concept of classic Windows Media Player visualizations, but all presets must be independently designed.

Do not copy Microsoft's preset names, shaders, visual assets, binaries, textures or code.

---

# 32. Visualizer Modes

Initial presets:

- Spectrum Flow
- Neon Wave
- Orbit Spectrum
- Pulse Rings
- Particle Bloom
- Aurora Ribbon
- Prism Tunnel
- Album Pulse
- Minimal Spectrum

---

# 33. Visualizer Architecture

```text
Decoded audio PCM
      ↓
IVisualizerAudioSource
      ↓
AudioAnalyzer
      ↓
FFT
RMS
Peak
Band Energy
Transient Energy
      ↓
AudioAnalysisFrame
      ↓
Latest-frame bounded channel
      ↓
Visualizer Renderer
      ↓
Win2D
      ↓
Optional Direct3D effect layer
```

The playback thread MUST NOT wait for the visualizer.

Drop visualizer frames before dropping audio.

---

# 34. DSP Requirements

Support FFT size 2048/4096, Hann window, logarithmic frequency mapping, mono/stereo input, RMS, peak, bass/mid/high energy, spectral bins and transient energy.

Never assume 44.1 kHz.

Use actual input sample rate.

---

# 35. Visualizer Performance

Targets:

- 60 FPS normal,
- 30 FPS power saver,
- <100 ms perceived audio-to-visual response,
- automatic graphics-quality reduction.

Adaptive quality may reduce FFT band count, particle count, blur radius, shader complexity and frame rate.

Never reduce playback audio quality to improve visualizer rendering.

---

# 36. Visualizer Accessibility

Include:

- Reduced Motion
- Disable Flashing
- Static Artwork
- Low Animation
- 30 FPS
- Visualizer disabled

Respect Windows reduced-motion preferences where possible.

---

# 37. Visualizer Testing

Golden synthetic signals:

```text
silence
100 Hz
1 kHz
10 kHz
left channel only
right channel only
white noise
impulse
```

Also test pause, resume, seek, source change, sample-rate change, resize, DPI change, GPU reset/device loss and renderer backlog.

---

# 38. OpenAI AI Features

Planned optional capabilities:

- transcription,
- automatic subtitles,
- transcript viewer,
- automatic chapters,
- media summary,
- key points,
- action items,
- Ask the Media,
- semantic transcript search,
- subtitle translation.

---

# 39. AI Architecture

All OpenAI access lives in `PlaySphere.AI`.

No UI ViewModel can instantiate OpenAI client directly.

Define `IAiMediaService`.

---

# 40. AI Privacy

Before uploading audio/transcript:

- require explicit user action,
- explain what leaves the device,
- allow cancellation,
- obey Local Only mode.

Never upload full video when audio is sufficient.

Secrets must use Windows secure credential storage.

---

# 41. Ask-the-Media

Answers must return clickable timestamp evidence.

Do not claim support unless transcript evidence exists.

---

# 42. SQLite Minimum Schema

Tables:

```text
media_items
playback_history
bookmarks
chapters
playlists
playlist_items
library_folders
transcripts
transcript_segments
ai_artifacts
conversion_jobs
```

All migrations must be versioned.

---

# 43. Security Rules

Treat media files, subtitles, playlists, URLs, metadata, thumbnails, archives and streams as untrusted.

Mandatory protections:

- path canonicalization,
- file size limits,
- parser limits,
- URL scheme validation,
- FFmpeg timeout,
- process cancellation,
- no shell injection,
- bounded logs,
- secret redaction,
- dependency vulnerability checks.

---

# 44. Performance Targets

Reference targets:

```text
Cold app launch: <= 3 seconds
Common local file open-to-first-frame: <= 2 seconds
Timeline feedback: <100 ms
UI: 60 FPS target
Library: 10,000 items without navigation lockup
```

These are engineering targets, not guarantees across every codec/device.

---

# 45. Accessibility

Must include keyboard navigation, visible focus, accessible names, high-contrast compatibility, color-independent states, subtitle scaling, reduced motion and screen-reader compatible core transport controls.

---

# 46. Skills Required for Pickle / Coding Agents

Create repository Skills under the Skill directory supported by the chosen Pickle backend.

Each Skill should contain `SKILL.md`.

## P0 required skills

### `playsphere-architecture`
Repository boundaries, dependencies, architecture enforcement.

### `dotnet-winui3`
C#, .NET 10, WinUI 3, Windows App SDK, XAML, MVVM, DPI, packaging.

### `media-libvlc-ffmpeg`
Playback adapter, native lifecycle, FFmpeg, FFprobe, codecs, subprocess safety and media fixtures.

### `playsphere-testing`
xUnit, integration tests, media tests, Windows UI Automation and performance tests.

### `playsphere-security`
Untrusted files, path safety, process invocation, credential safety and native dependency review.

### `audio-dsp-visualizer`
FFT, sample rates, Hann windows, spectrum bands, RMS, peaks, Win2D rendering, latency and DSP golden tests.

## P1 skills

### `sqlite-efcore`
Schema design, migrations, indexes and FTS5.

### `modern-windows-ui`
Vibrant design tokens, adaptive layout, Fluent-inspired behavior, animation and accessibility.

### `openai-media-ai`
Transcription, Responses API, embeddings, transcript retrieval, timestamp evidence and safe AI integration.

### `windows-performance`
ETW, dotnet-trace, memory, startup and CPU/GPU profiling.

### `windows-packaging`
MSIX, code signing, auto-update and installer testing.

## P2 skills

Add later:

- localization,
- observability,
- crash reporting,
- advanced GPU shaders,
- audio fingerprinting,
- network-stream diagnostics,
- accessibility audit,
- release engineering.

---

# 47. MCP Servers Required

MCPs provide documentation, repository context and development services.

Do not confuse MCPs with runtime libraries.

## P0 MCPs

### GitHub MCP

Use for repository, issues, pull requests, Actions, code review and security alerts.

Start with limited repository scope.

Avoid automatic merges.

### Microsoft Learn MCP

Use for current WinUI 3, Windows App SDK, .NET, MSIX, Windows APIs, accessibility and graphics documentation.

Prefer Microsoft primary documentation.

### OpenAI Developer Documentation MCP

Use for current Codex/OpenAI API, Responses API, transcription, embeddings and model configuration.

Do not rely on stale hard-coded API assumptions.

## P1 MCPs

### Figma MCP

Use once product designs are managed in Figma.

Purposes:

- inspect frames,
- retrieve components,
- design variables,
- icons,
- spacing,
- layout specifications.

### Context7 MCP

Optional for current third-party library documentation when primary docs are insufficient.

Never treat it as more authoritative than official documentation.

## P2 MCPs

### Sentry MCP

Add after production monitoring exists.

### Custom PlaySphere Media QA MCP

Eventually provide tools such as:

```text
probe_media
validate_media_fixture
run_playback_smoke_test
inspect_ffprobe
generate_synthetic_audio
analyze_fft_result
validate_visualizer
validate_subtitle
validate_transcode
```

---

# 48. Plugins Recommended

## GitHub connector/plugin
Repository workflow.

## Figma plugin
UI design handoff.

## OpenAI Developers plugin
OpenAI-backed feature development and debugging.

## Security review plugin
Use a trusted security-analysis plugin before release hardening.

Do not install arbitrary community plugins without reviewing source, permissions, network behavior, maintenance and security posture.

---

# 49. Pickle Skill Packs

If using a Pickle Rick-style workflow, useful skill categories include:

- PRD drafting,
- code implementation,
- review,
- simplification,
- conformance checking,
- testing,
- security auditing.

PlaySphere repository Skills override generic patterns where they conflict.

---

# 50. Pickle Development Loop

For every work package:

```text
RESEARCH
   ↓
PLAN
   ↓
IMPLEMENT
   ↓
BUILD
   ↓
TEST
   ↓
VERIFY
   ↓
REVIEW
   ↓
SIMPLIFY
   ↓
DOCUMENT
```

Never skip VERIFY.

---

# 51. Work Package Rules

Every ticket must specify:

```text
Goal
Files/modules allowed
Files/modules prohibited
Architecture constraints
UX acceptance criteria
Tests required
Performance considerations
Security considerations
Definition of done
```

Do not ask the agent: “Build the media player.”

Use small tickets.

---

# 52. Implementation Sequence

## Phase 0 — Repository Foundation

- PS-001 Create solution structure.
- PS-002 Create build scripts and CI.
- PS-003 Create agent rules.
- PS-004 Create design-token resource dictionaries.
- PS-005 Create app shell/navigation.

Gate: restore succeeds, build succeeds, tests succeed and app launches.

## Phase 1 — Playback Core

- PS-101 Define `IMediaPlaybackEngine`.
- PS-102 Integrate libVLC adapter.
- PS-103 Implement media load.
- PS-104 Play/pause/stop.
- PS-105 Seek.
- PS-106 Speed.
- PS-107 Volume/mute.
- PS-108 Track enumeration.
- PS-109 Error recovery.

Gate: play controlled MP4, MKV and MP3.

## Phase 2 — Vibrant Player UI

- PS-201 Build modern player screen.
- PS-202 Create transport controls.
- PS-203 Implement timeline.
- PS-204 Add hover/active animations.
- PS-205 Implement fullscreen/cinema.
- PS-206 Implement mini-player.
- PS-207 Add drawer system.

Gate: UI works at 1366×768, 1920×1080, 2560×1440 and 4K high DPI.

## Phase 3 — Subtitles and Audio

Implement subtitle tracks, external SRT, styling, subtitle delay, audio tracks, audio delay, output device and equalizer.

> Status: subtitle styling (font/color/outline/text-scale/position) is engineering-complete and
> verified (ADR-0006); manual on-screen QA pending. Equalizer, audio output device, channel/downmix,
> loudness normalization and preferred-language auto-selection are DONE. Stereo balance is not
> implementable via the mandated libVLC engine (see AGENTS.md).

## Phase 4 — Media Library

Implement SQLite, folder selection, scanning, metadata, thumbnails, recent, favorites, continue watching and search.

## Phase 5 — Playlists and Bookmarks

Implement playlist creation, reorder, shuffle, repeat, bookmarks, chapters and persistence.

## Phase 6 — Visualizer

- VIS-01 Create Visualizer module.
- VIS-02 Implement PCM analysis source.
- VIS-03 Implement FFT/RMS/peak analysis.
- VIS-04 Create Win2D rendering engine.
- VIS-05 Implement Spectrum Flow.
- VIS-06 Implement Neon Wave.
- VIS-07 Implement Orbit Spectrum.
- VIS-08 Implement Pulse Rings.
- VIS-09 Implement Particle Bloom.
- VIS-10 Fullscreen visualizer mode.

Gate: playback never waits for renderer.

## Phase 7 — Media Tools

Implement screenshot, waveform, hover thumbnails, Mark In/Out, conversion and clip export.

## Phase 8 — Network Playback

Implement URL dialog, HTTP/HLS/DASH/RTSP, buffering status and reconnect behavior.

## Phase 9 — AI Media

Implement only after core playback is stable.

- AI-01 OpenAI service boundary.
- AI-02 Audio extraction.
- AI-03 Transcription.
- AI-04 Transcript UI.
- AI-05 Timestamp search.
- AI-06 Automatic chapters.
- AI-07 Summaries.
- AI-08 Ask-the-Media.
- AI-09 Subtitle translation.

## Phase 10 — Release Engineering

Implement MSIX, signing, clean-machine install, update mechanism, crash diagnostics, dependency license audit, SBOM and privacy documentation.

---

# 53. Required Commands

Before claiming completion:

```powershell
dotnet restore
dotnet build -c Release
dotnet test -c Release
```

If native media code changed: run playback smoke tests.

If visualizer changed: run DSP golden tests.

If UI changed: run Windows UI Automation tests and manual high-DPI check.

If packaging changed: run clean VM install/uninstall test.

---

# 54. Testing Rules

Never:

- delete a failing test simply to make CI pass,
- weaken expected behavior without approved requirement change,
- replace a golden expected result with current broken output,
- mock the feature being tested in integration tests.

---

# 55. Media Test Corpus

Maintain small, legally usable fixtures:

- H.264/AAC MP4,
- MKV multi-audio,
- MKV multi-subtitle,
- MP3,
- FLAC,
- WAV,
- VFR video,
- portrait video,
- SRT,
- ASS,
- VTT,
- corrupted media,
- truncated media,
- malformed playlist.

Never commit copyrighted movies/music as test data.

---

# 56. Definition of Done

A ticket is complete only when:

- requirement implemented,
- architecture respected,
- tests added,
- tests pass,
- build passes,
- cancellation works,
- errors handled,
- security checked,
- UI checked if relevant,
- docs updated,
- no known critical regression.

---

# 57. Prohibited Shortcuts

Do not:

- put everything into MainWindow,
- call libVLC directly from ViewModels,
- invoke FFmpeg through raw shell strings,
- hard-code model IDs across the app,
- store API keys in settings JSON,
- scan all user drives automatically,
- silently upload media,
- silently overwrite source media,
- block UI on file scanning,
- block playback for visualizer rendering,
- suppress errors without logging,
- copy third-party UI assets.

---

# 58. ADR Requirements

Create an ADR before changing playback engine, database, native UI framework, AI provider architecture, packaging mechanism, media-processing model, visualizer GPU technology or major repository boundaries.

---

# 59. UI Acceptance Checklist

Every screen must be checked for:

- modern visual hierarchy,
- vibrant accents without clutter,
- consistent spacing,
- keyboard operation,
- dark/light contrast,
- high DPI,
- resizing,
- loading state,
- empty state,
- error state,
- disabled state,
- hover state,
- focus state.

---

# 60. Product Quality Bar

PlaySphere is not complete simply because it can decode video.

Priority order:

1. playback stability,
2. startup speed,
3. seek responsiveness,
4. subtitle quality,
5. visual polish,
6. low CPU/GPU overhead,
7. reliability,
8. accessibility,
9. media library usability,
10. AI features.

AI is deliberately last.

---

# 61. Final Instruction to Pickle / Coding Agent

You are building **LiVi PlaySphere Studio**, a commercial-quality Windows application developed by **LiVi Software Company**.

Do not optimize for producing the maximum amount of code.

Optimize for correctness, maintainability, architecture, testability, media reliability, user experience, performance, privacy and security.

For every task:

1. Read this file.
2. Read relevant ADR/module docs.
3. Load relevant repository Skills.
4. Inspect existing implementation before writing code.
5. Make the smallest coherent change.
6. Add tests.
7. Run verification.
8. Review architecture/security/performance.
9. Simplify where possible.
10. Report exact limitations.

Never declare a feature complete solely because the code compiles.

---

# 62. Recommended First Pickle Task

```text
Implement PS-001 through PS-005 only.

Goal:
Create the PlaySphere solution foundation and modern vibrant application shell.

Requirements:
- .NET 10
- WinUI 3
- repository structure from PICKLE.md
- design tokens
- left navigation:
  Home
  Library
  Music
  Playlists
  Streams
  AI Media
- vibrant dark theme
- responsive shell
- no libVLC integration yet

Required tests:
- solution builds
- unit test project runs
- app launch smoke test
- navigation route tests where practical

Do not:
- implement playback
- implement AI
- install unnecessary packages
- bypass repository boundaries

At completion report:
- files created
- packages added
- commands run
- test results
- known limitations
```

---

## END OF AUTHORITATIVE PLAYSPHERE PICKLE BLUEPRINT
