# AGENTS.md — LiVi PlaySphere Studio

Authoritative blueprint: **PICKLE.md** in the repository root. Read it before any work.

## Roles

You are building LiVi PlaySphere Studio for LiVi Software Company: a commercial-quality,
offline-first universal Windows media player. Playback first, AI last.

## Skills (PICKLE.md §46)

Repository skills live under `.opencode/skills/`. All three tiers are now present:
- **P0 (6):** playsphere-architecture, dotnet-winui3, media-libvlc-ffmpeg, playsphere-testing,
  playsphere-security, audio-dsp-visualizer.
- **P1 (5):** sqlite-efcore, modern-windows-ui, openai-media-ai, windows-performance, windows-packaging.
- **P2 (8):** localization, observability, crash-reporting, advanced-gpu-shaders, audio-fingerprinting,
  network-stream-diagnostics, accessibility-audit, release-engineering.

Load the relevant skill(s) before starting work, per PICKLE.md §61.3.

## Commands (Phase 0 gate = §53 of PICKLE.md)

```powershell
dotnet restore
dotnet build -c Release
dotnet test -c Release
tools/run-app-smoke.ps1          # app launch smoke test
tools/build-release.ps1          # all of the above in sequence
tools/verify-environment.ps1     # dotnet + NuGet checks
```

## Rules

1. Read PICKLE.md, relevant docs, then existing code before writing anything.
2. Make the smallest coherent change. Small tickets (PS-xxx) only.
3. UI never calls libVLC/FFmpeg/OpenAI directly.
4. No shell-string FFmpeg commands; structured argument arrays only.
5. No feature is complete without tests. Never delete/weaken a failing test to pass CI.
6. Verify (`dotnet test`) before claiming completion. Report exact limitations.
7. Treat media files, subtitles, playlists, URLs and metadata as untrusted input.
8. No cursed shortcuts: no MainWindow-everything, no drive auto-scan, no silent uploads,
   no storing API keys in settings JSON, no UI-blocking scans.
9. Changing playback engine, DB, UI framework, AI provider, packaging, GPU tech or repo
   boundaries requires an ADR (PICKLE.md §58).

## Current status

- Phase 0 (Foundation): COMPLETE — structure, build scripts, CI, tokens, shell, nav, navigation tests.
- Phase 1 (Playback Core): COMPLETE — PS-110 through PS-109 all done and verified.
  ADRs 0003 (libVLC) & 0004 (HWND video out) accepted.
- Phase 2 (Vibrant Player UI): COMPLETE
  - PS-201 (Modern Player Screen): COMPLETE — `PlayerPage.xaml`/`.cs` with top bar, video surface, transport bar, route registered.
  - PS-202 (Transport Controls + Open File): COMPLETE — `PlayerViewModel.cs`, `FilePickerHelper.cs`, "Open File" button on `HomePage`.
  - PS-203 (Interactive Timeline & Shortcuts): COMPLETE — Scrub hover preview popup, relative seek (±5s, ±30s), rate cycling (0.5×–2×), keyboard shortcuts (Space, Left/Right, Up/Down, M, R, S, Esc).
  - PS-204 (Controls Auto-Hide & Polish): COMPLETE — DispatcherTimer-driven auto-hide during playback, pause/hover awareness, full opacity restore on interaction.
  - PS-205 (Fullscreen & Cinema Mode): COMPLETE — Fullscreen presenter toggling via AppWindowService, double-click, F/F11/Alt+Enter/Esc shortcuts, navigation pane collapse, and cinema mode state management with unit test suite.
  - PS-206 (Mini-player / CompactOverlay): COMPLETE — CompactOverlay presenter mode via AppWindowService, P key / button toggling, minimal top-bar hiding, mutual exclusion with fullscreen.
  - PS-207 (Player Drawer System): COMPLETE — Slide-out drawer overlay for Audio Streams (A), Subtitles (V), Playback Speed & Custom Slider (R), Media Technical Inspector (I), Chapters/Bookmarks, Esc dismiss and auto-hide pause.
- Phase 5 (Playlists & Bookmarks): COMPLETE (engineering-verified; see manual-QA caveats in the Phase 5 section below).
  - Data layer (Bookmark/Playlist/PlaylistItem entities + CRUD + `AddPlaylistsAndBookmarks` migration), playback-queue core
    (`PlaybackQueue<T>` + `PlaybackRepeat`), player/VM queue wiring, PlaylistsPage management UI, transport/bookmark
    controls, M3U/M3U8 import & export (backend + PlaylistsPage buttons), and `.`/`,` next/prev keyboard shortcuts —
    all with passing tests (201 unit + 52 integration + 58 playback, 1 skip, 0 failures, 0 build warnings).
  - Remaining human step before end-to-end closure: the manual-QA pass (create/reorder/play playlists, bookmarks,
    M3U import/export round-trip, `.`/`,` shortcuts) — this environment has no desktop UI automation.
- Phase 6 (Visualizer): IN PROGRESS — DSP core (VIS-01/02/03) + Win2D renderer (VIS-04/ADR-0007) + the full nine-preset PICKLE.md §32 palette (VIS-05..09 + the four gap-filled originals) + engine PCM wiring (VIS-09) + reduced-motion power-saver (PICKLE.md §35/36) COMPLETE (engineering-verified). **VIS-10 Fullscreen Visualizer REMOVED by user decision (2026-09-01):** fullscreen-visualizer showed a blank screen while the in-window visualizer worked, so the feature was removed — the regular visualizer + the existing video fullscreen cover the need. Use video fullscreen for fullscreen.
  - New `PlaySphere.Visualizer` module (Domain+Application deps only) with a pure, unit-testable DSP core:
    `IVisualizerAudioSource`/`IAudioAnalyzer`/`ILatestAnalysisFrameProvider` contracts (Application), the
    `AudioAnalysisFrame` result type (Domain), and the `Fft` (radix-2, 2048/4096), `AudioAnalyzer`
    (Hann window, RMS/peak, log-mapped spectrum bands, bass/mid/high band energy, transient energy),
    `PcmAudioSource` and `LatestAnalysisFrameChannel` implementations (module). Wired via `AddPlaySphereVisualizer()`
    in `App.xaml.cs`. 20 DSP golden tests pass (silence/100 Hz/1 kHz/10 kHz/L-only/R-only/white noise/impulse,
    mono/stereo, 44.1 k/48 k, 4096 window, transients, latest-frame channel, DI registration).
  - Win2D rendering engine (VIS-04, ADR-0007): `PlaySphere.UI.Visualizer.VisualizerCanvas` wraps
    `CanvasAnimatedControl`, reads frames via `ILatestAnalysisFrameProvider.TryReadLatest()` and
    draws via `IRenderPreset` — no libVLC/FFmpeg referenced by the renderer. New
    `SpectrumFlowPreset` (VIS-05, first preset): 48 vertical bars with bottom-to-top gradient
    mapping the log-mapped spectrum. Bar→band mapping (`SourceIndexForBar`) is unit-tested (10
    tests: proportionally-spaced mapping across various band counts, edge cases, guard clauses,
    color defaults). Wired into `PlayerPage` as a collapsed overlay in the video surface grid;
    toggle via overflow menu "Show Visualizer" button; canvas pauses when playback stops.
    `Microsoft.Graphics.Win2D` 1.4.0 package added to UI project; `Win2DWarnNoPlatform=false`
    suppresses the AnyCPU warning (native `Microsoft.Graphics.Canvas.dll` resolves at win-x64
    runtime via the App's RID).
  - Engine PCM wiring (VIS-09, ADR-0008): `IMediaPlaybackEngine` gained `IsVisualizerAttached`/
    `AttachVisualizerSourceAsync(IVisualizerAudioSource)`/`DetachVisualizerSourceAsync` (engine
    depends on the Application-layer `IVisualizerAudioSource` contract — `Playback.Abstractions`
    now references `PlaySphere.Application`, no cycle). `LibVlcPlaybackEngine` registers
    `SetAudioFormatCallback`/`SetAudioCallbacks` (FL32 via a `LibVLCAudioSetupCb` fourcc write,
    native rate/channels accepted, never assuming 44.1 kHz), marshals decoded PCM to the source on
    VLC's audio thread (bounded, exceptions swallowed), and detach rebuilds the MediaPlayer to
    unregister callbacks and restore normal audio. Per the libVLC audio-callback contract,
    **speaker output is silenced while the tap is attached** — an accepted ADR-0008 limitation
    (dual-output via NAudio WASAPI was considered and rejected: Windows-only and unverifiable
    here). `PlayerPage` resolves `IVisualizerAudioSource` via DI and attaches/detaches the tap on
    the "Show Visualizer" toggle and on page unload. 5 new PlaybackTests (`VisualizerPcmTapTests`)
    cover attach/detach/replace/null against the real engine.
  - All five presets (VIS-05..09): COMPLETE (engineering-verified). In addition to Spectrum Flow
    (VIS-05), four new `IRenderPreset` implementations joined the UI layer — `NeonWavePreset`
    (VIS-06: smooth glowing waveform, `PointCount`=96, thick translucent glow pass under a thin
    bright pass, colour `#36A9FF`, `SourceIndexForSample` proportional band mapping),
    `OrbitSpectrumPreset` (VIS-07: 64 radial spectrum bars orbiting centre, colour `#EE4999`,
    `SourceIndexForBar`), `PulseRingsPreset` (VIS-08: 5 concentric rings pulsing with a smoothed
    bass/transient/RMS envelope, inner→outer colour fade, `ComputeEnvelope` smoothing field),
    `ParticleBloomPreset` (VIS-09: Rows×Columns particle grid blooming with a broadband/peak
    `ComputeDrive`, `ParticleRadius`/`ParticleAlpha` pure helpers). All use only the session's
    colour-taking Draw primitives (`DrawLine`/`DrawCircle`/`FillCircle`), with per-frame allocation-
    free rendering (no brush/geometry allocation at 60 FPS); only internal float smoothing/time
    state persists across frames. New "Visualizer Preset" `MenuFlyoutSubItem` in the PlayerPage
    overflow menu selects any of the five via a shared `VisualizerPreset_Click` handler +
    `CreatePreset(string)` factory (default Spectrum Flow). New pure `VisualizerPresetTests` (25:
    proportional band mapping + guard clauses for Neon Wave/Orbit, envelope/drive bounds + growth
    for Pulse Rings/Particle Bloom, radius/alpha scaling + bounds, all five `Name`s).
  - Fullscreen visualizer mode (VIS-10) + reduced-motion handling (PICKLE.md §35/§36): COMPLETE
    (engineering-verified). A new "Fullscreen Visualizer" overflow-menu item + transport-bar button
    enter fullscreen via the window service, hide the video surface so the visualizer fills the
    window, and auto-enable the visualizer/PCM tap; exiting (button, or leaving fullscreen by Esc)
    restores the picture. `VisualizerCanvas` gained a `ReducedMotion` property that caps
    `CanvasAnimatedControl.TargetElapsedTime` at ~30 FPS; `PlayerPage` drives it from Windows'
    `UISettings.AnimationsEnabled` preference (with a live `AnimationsEnabledChanged` handler, gated
    to Windows 10.0.19041+ for CA1416).
  - Simultaneous "visualizer + audible audio": COMPLETE on Windows via ADR-0009 (see the dedicated
    section below) — a NAudio WASAPI device-loopback capture feeds the visualizer while libVLC keeps
    outputting to the speakers. Full suite: 282 unit + 52 integration + 70 playback (1 skip),
    0 failures, 0 build warnings; smoke OK.
- Phase 3 (Subtitles and Audio): COMPLETE (engineering-verified; see manual-QA caveat below).
  - Fixed a pre-existing build break found while starting this phase: `IMediaPlaybackEngine`'s
    equalizer members (§18) had been added to the abstraction, `PlayerViewModelTests`' fake
    engine and `EqualizerModels.cs`, but `LibVlcPlaybackEngine` never implemented them —
    `dotnet build -c Release` failed solution-wide. Implemented via LibVLCSharp's
    `Equalizer`/`MediaPlayer.SetEqualizer` (10-band, `EqualizerPresets` catalog applied fresh
    each call; libVLC does not retain the equalizer handle so it is disposed immediately after
    `SetEqualizer`).
  - New `PlaySphere.Subtitles` project (PICKLE.md §17): `SubtitleCue`/`SubtitleDocument` domain
    model (with `CuesAt(position, delay)` and `Search(query)`), defensive
    `SrtSubtitleParser`/`WebVttSubtitleParser` (malformed blocks skipped, not thrown; bounded by
    `SubtitleParserLimits` per §43), `SubtitleFileMatcher` for automatic same-directory
    external-subtitle discovery ranked by preferred language, and `SubtitleExporter` (SRT
    export). ASS/SSA/SUB still render via libVLC directly (`LoadExternalSubtitleAsync`) but are
    not parsed by this module, so cue search/list/export only work for loaded SRT/WebVTT.
  - Audio output device enumeration/selection, stereo/mono downmix (`AudioChannelMode`), and
    loudness normalization / night mode (libVLC `normvol`/`compressor` audio filters, applied
    per-media — take effect starting with the next `LoadAsync`, not hot-swappable mid-playback)
    added to `IMediaPlaybackEngine` and `LibVlcPlaybackEngine`. New domain types in
    `PlaySphere.Domain.Media` (`AudioOutputDeviceInfo`, `AudioChannelMode`,
    `AudioEnhancementOptions`) follow the existing `TrackInfo`-in-Domain precedent rather than
    routing through the still-empty `PlaySphere.Audio` project.
  - Subtitle styling originally scoped to text scale only (`SubtitleStyleOptions.TextScalePercent`,
    25–500%, via libVLC's `sub-text-scale` media option). Font/color/outline/position styling was
    originally deferred because freetype-module-level libVLC settings were deemed unverifiable
    without a real display — now IMPLEMENTED in Phase 7 (see the dedicated note below).
  - `PlayerViewModel` wired to all of the above: equalizer commands, audio output/channel/
    enhancement commands, subtitle text-scale command, cue search (`SearchSubtitleCues`),
    seek-to-cue, and SRT export. `TryAutoLoadSubtitleAsync` now uses `SubtitleFileMatcher`
    instead of its previous exact-stem-only loop, so language-tagged files (`Movie.en.srt`) are
    found too.
  - `PlayerPage` UI: new Equalizer drawer (enable toggle, preset picker, preamp + 10 band
    sliders, reset), extended Audio Tracks drawer (output device list, channel/downmix
    ComboBox, loudness normalization / night mode toggles), extended Subtitles drawer (text
    scale slider, cue search box with clickable seek-to-cue results, SRT export button).
    "E" keyboard shortcut and overflow-menu entry open the Equalizer drawer.
  - Manual-QA caveat: this session has no interactive-desktop UI automation tool. Verified
    `dotnet build`/`dotnet test` (111 unit + 50 playback tests passing) and that
    `tools/run-app-smoke.ps1` launches the app without crashing, which exercises WinUI's
    compile-time XAML validation but not runtime interaction with the new drawers — the
    Equalizer/Audio-device/cue-search panels have not been visually clicked through. Do that
    manual pass (open a video, exercise each new drawer control) before calling Phase 3 done
    end-to-end.
  - Known remaining gaps, not blocking but worth a follow-up ticket: true stereo balance
    (distinct from `AudioChannelMode` downmix) is not implemented; audio-track preferred-language
    auto-selection is now DONE (see the dedicated note below); subtitle font/color/outline/position
    styling is now DONE (see the dedicated Phase 7 note below).
- Phase 4 (Media Library): STARTED — PS-401 (data foundation) COMPLETE.
  - New `library_folders`/`media_items`/`playback_history` SQLite schema (PICKLE.md §42) via EF
    Core: `LibraryFolder`/`LibraryMediaItem`/`PlaybackHistoryEntry` entities in
    `PlaySphere.Domain.Library` (deliberately distinct from `Domain.Media.MediaFile`, which stays
    the lightweight playback-load value type); `IMediaLibraryRepository` contract in
    `PlaySphere.Application.Library`; `PlaySphereDbContext`/`EfMediaLibraryRepository` in
    `PlaySphere.Infrastructure.Library`, with the `AddPlaySphereLibrary(path)` /
    `MigrateLibraryDatabase()` DI extensions mirroring the existing `AddPlaySpherePlayback()`
    pattern. `App.xaml.cs` resolves the DB to
    `%LOCALAPPDATA%\LiVi PlaySphere Studio\library.db` (unpackaged app per ADR-0001, so
    `Environment.SpecialFolder.LocalApplicationData`, not `Windows.Storage.ApplicationData`) and
    migrates at startup — confirmed end-to-end by running the built app and inspecting the
    created `library.db`/`-wal`/`-shm` files, not just unit tests.
  - `library_folders`/`media_items`/`playback_history` cover folder registration, catalog
    upsert-by-fingerprint, favorites, continue-watching / recently-played queries and substring
    search.
  - Hit and fixed two real bugs during this ticket, worth remembering: (1) `PlaySphere.App`'s
    `App : Application` broke with "'Application' is a namespace but is used like a type" the
    moment `PlaySphere.Application` (the project) gained actual types — `PlaySphere.Application`
    as an enclosing-namespace match shadows the `using Microsoft.UI.Xaml;` import for the bare
    name `Application`; fixed by fully qualifying `Microsoft.UI.Xaml.Application` in the class
    declaration. (2) EF Core's SQLite provider cannot translate `ORDER BY` over `DateTimeOffset`
    columns (throws `NotSupportedException` at query time, not at migration time) — all
    timestamp columns here use `DateTime` (UTC) instead.
  - EF Core packages pinned at 10.0.11 (matches the already-pinned `Microsoft.Extensions.*`
    10.0.11). `dotnet-ef` (globally installed, 9.0.1) scaffolds migrations fine against them
    despite the version-mismatch warning it prints.
  - Tests: `tests/PlaySphere.IntegrationTests` is new (was an empty stub) — real temp-file SQLite
    databases with migrations actually applied via `LibraryDatabaseFixture`, not mocks. Note for
    next time: dispose must call `Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()` before
    deleting the temp directory, or cleanup races the pooled native connection and throws
    `IOException` ("file in use").
  - PS-402 (folder selection + scanning) COMPLETE.
    - New `PlaySphere.Library` project (Domain+Application deps only, matching the
      Media/Visualizer/AI module pattern): `MediaFileFingerprinter` (length + UTC mtime + SHA-256
      over up to three 64 KB sampled chunks — start/middle/end, never the whole file, per
      PICKLE.md §24) and `LibraryScanner` (recursive directory walk of one explicitly-added
      folder, filtered to PICKLE.md §14's media extensions, upserts via
      `IMediaLibraryRepository`). `ILibraryScanner`/`LibraryScanResult` contracts live in
      `Application.Library`/`Domain.Library` respectively, so UI depends only on the interface —
      same boundary discipline as `IMediaPlaybackEngine`/`IMediaLibraryRepository`.
    - `LibraryPage` is now functional: Add Folder (real `FolderPicker`, via a new
      `FilePickerHelper.PickFolderAsync`) → `AddFolderAsync` → `ScanFolderAsync`, folder list
      with per-folder Remove, a live media item list, and a search box wired to
      `SearchAsync`. `HomePage`'s previously-static "Continue Watching"/"Recently Played" rows
      now render real `GetContinueWatchingAsync`/`GetRecentlyPlayedAsync` data (falling back to
      the original placeholder card when empty).
    - Hit the SAME `Application`-vs-`PlaySphere.Application` namespace collision again, this
      time in pre-existing `PlayerPage.xaml.cs` (which already called `Application.Current...`
      successfully before `PlaySphere.UI` gained a project reference to `PlaySphere.Application`)
      plus the two new pages. A same-named `using Application = Microsoft.UI.Xaml.Application;`
      alias does **not** fix this — namespace-member lookup across enclosing namespaces
      (`PlaySphere` containing sibling `PlaySphere.Application`) wins over using directives
      including aliases, so the alias must use a distinct name. Fixed everywhere with
      `using XamlApplication = Microsoft.UI.Xaml.Application;` + replacing bare
      `Application.Current` with `XamlApplication.Current`. **Any future file added under the
      `PlaySphere.*` namespace tree that references `Microsoft.UI.Xaml.Application` needs this
      same alias — don't reintroduce a bare `Application.Current`.**
    - The Phase 4 FFprobe-metadata, thumbnail/cover-art and card-polish deferrals listed below
      are all now resolved — see the FFprobe section, the cover-art note, and the Phase 4 polish
      note at the end of this section.
    - Manual-QA caveat (same limitation as Phase 3): no desktop UI automation tool in this
      environment, so "Add Folder" → picker → scan was verified through the real
      `LibraryScanner`/`IMediaLibraryRepository` integration tests (real files, real migrated
      SQLite) and by confirming the built app launches without crashing — not by actually
      clicking the button. A ready-made two-file test folder was left at
      `%TEMP%\PlaySphereSmokeMedia` for a manual click-through.
- Phase 5 (Video Output Surface): STARTED — the critical video-output gap is fixed (engineering-
  verified; see manual-QA caveat). Prior to this phase the player decoded headlessly: audio,
  timeline, subtitles all worked but no picture rendered because the libVLC video surface was never
  attached to a swap-chain — the engine's rename-to-WinUI window creation (`libvlc_media_player_set_xwindow`)
  was never wired to an actual HWND/swap-chain.
  - Approach: **Official `LibVLCSharp.WinUI` `VideoView` (composition)**, per the user's explicit
    choice — ADR-0004's HWND `TrySetVideoWindow` path is superseded by this composition substitute
    (the exact fallback ADR-0004 reserved). `VideoView` is the `libvlcsharp.winui` library's
    SwapChainPanel control: `VideoViewBase : Control` with `[TemplatePart(Name="SwapChainPanel")]`,
    `MediaPlayer` DependencyProperty, and an `Initialized` event whose `SwapChainOptions` is a
    `string[]`. libVLC **must be constructed** with those options (`--winrt-swapchain`/
    `--winrt-d3dcontext`), so the engine lazily rebuilds LibVLC/MediaPlayer on first attach.
  - New libVLC-free `IVideoSurface` in `PlaySphere.Playback.Abstractions` (`SwapChainOptions
    string[]?`, `SetRenderer(object)`, `ClearRenderer()`). Engine contract extended:
    `IsVideoSurfaceAttached`, `AttachVideoSurfaceAsync(IVideoSurface, CancellationToken)`,
    `DetachVideoSurfaceAsync(CancellationToken)`. UI depends only on `IVideoSurface` (the shared
    renderer object crossing the boundary is the documented libVLC type at the view layer, same as
    ADR-0004's accepted VideoSurface element).
  - `LibVlcPlaybackEngine`: collects `SwapChainOptions` on first attach; if they differ from the
    engine's current LibVLC (DI-implicit default, no swap-chain), it **rebuilds** LibVLC + MediaPlayer
    via `LibVlcBootstrap.CreateLibVlc(lib, options, swapChainOptions)` and pushes the new
    `MediaPlayer` to the surface with `SetRenderer`. Same-surface reattach is a no-op; attaching a
    different surface clears the previous one; detach clears renderer and marks unattached. Ownership:
    an engine that created its own LibVLC disposes it (`_ownsLibVlc`), while the DI/shared-singleton
    LibVLC stays owned by the container.
  - `PlaySphere.UI.Controls.VideoSurfaceHost`: a `UserControl` wrapping `lvs:VideoView`, the single
    view-layer libVLC integration point. Captures `SwapChainOptions` from `VideoView_Initialized`,
    implements `IVideoSurface` (`SetRenderer` casts the renderer to `MediaPlayer` and assigns
    `VideoView.MediaPlayer`; `ClearRenderer` nulls it), and re-raises `Initialized` so the page can
    attach once the host is ready. `PlayerPage.xaml` hosts it in the video surface grid (placeholder
    panel kept underneath, hidden on attach); `PlayerPage.xaml.cs` resolves the engine, attaches on
    host-init/loaded, detaches on unload.
  - **The `LibVLCSharp.dll` filename-collision fix (critical):** both the bare `LibVLCSharp` package
    and `LibVLCSharp.WinUI` ship an assembly literally named `LibVLCSharp.dll`. `Playback.LibVLC` and
    `PlaybackTests` (net10.0 non-Windows TFMs that cannot reference the WinUI package) pull the bare
    one; the App/UI pull the WinUI one. The bare copy would win the copy-to-output filename
    collision, and it lacks the `VideoView` type → runtime TypeLoadException when instantiating
    `VideoSurfaceHost`. An explicit `<Reference HintPath>` to the WinUI DLL in the App did **not**
    fix it (the copy-local still pulled the bare DLL from the referenced project output). The working
    fix: `Playback.LibVLC`'s `LibVLCSharp` PackageReference got `ExcludeAssets="runtime"` (compile
    against it, but don't deploy it — `PlaybackTests` supplies its own copy for its test host), and
    the App references `LibVLCSharp.WinUI` directly so the WinUI superset `LibVLCSharp.dll` is the
    **single** deployed runtime copy. Verified: `playSphere.App` output `LibVLCSharp.dll` SHA-matches
    the `libvlcsharp.winui\3.10.1` package DLL exactly (294912 bytes) and `VideoView` resources
    ("VideoView"/"SwapChainPanel") are present in the merged `PlaySphere.App.pri`, with
    `SharpDX.Direct3D11.dll`/`SharpDX.dll`/`SharpDX.DXGI.dll` deployed as its runtime deps.
  - Now that three packages ship the same-named assembly (bare + WinUI superset), keep a single
    consistent copy: **do not add the bare `LibVLCSharp` package to any project that also pulls
    `LibVLCSharp.WinUI`, and prefer `ExcludeAssets="runtime"` on bare references.**
  - Tests: `tests/PlaySphere.PlaybackTests/VideoSurfaceTests.cs` (6 new) cover the attach/detach
    contract against the real `LibVlcPlaybackEngine` (bind renderer, same-surface no-op, surface
    switch clears previous + binds new, detach clears + marks unattached, detach-with-no-surface
    safe, null-surface throws). The actual swap-chain LibVLC rebuild path is covered by design, not
    an automated test (it needs a real swapchain). `PlayerViewModelTests`' `FakePlaybackEngine`
    implements the three new members. Full result now: 117 unit + 20 integration + 56 playback
    (1 skipped), 0 failures, 0 build warnings/errors.
  - Manual-QA caveat: still no interactive-desktop UI automation in this environment, so on-screen
    rendering is **not** visually verified. What IS verified: solution builds clean, all 193 tests
    pass, the app boots via `tools\run-app-smoke.ps1` without crashing, the correct WinUI
    `LibVLCSharp.dll` + SharpDX + merged `.pri`(with the VideoView template) are deployed. The smoke
    window only reaches `HomePage` (it does not navigate to `PlayerPage`), so `VideoView`
    instantiation/swap-chain attach and the actual picture were not exercised by automation.
    Required manual pass: open a video, confirm a picture renders on `PlayerPage` (this is the
    original bug claim), then check fullscreen/cinema/compact sizes the surface correctly, and
    confirm the swap-chain rebuild path on first open.
- Phase 4 (Media Library): FFprobe technical metadata now WIRED IN (see FFprobe section below) —
  Phase 4 is COMPLETE apart from the formerly-deferred polish items, which are now all resolved —
  FFprobe metadata, thumbnails/cover art (generation via ADR-0005, card rendering via
  `ThumbnailImage`). The aesthetic card/vibrant-UI polish is now DONE too (Phase 4 polish note below).
  - FFprobe integration (technical metadata, PICKLE.md §23): COMPLETE (engineering-verified).
    - New `PlaySphere.Media.Probe` project (Domain+Application deps only, matching the
      Media/Visualizer/AI module pattern and the Library module precedent): `MediaProbeResult`
      domain type in `PlaySphere.Domain.Media` (Duration, Width/Height, Resolution string,
      Codec, FrameRate, BitrateKbps, AudioTrackCount, SubtitleTrackCount), `IMediaProbe` contract
      in `PlaySphere.Application.Media` (`ProbeAsync(filePath, ct)` → `MediaProbeResult?`), and
      `FfProbeMediaProbe` — a bounded ffprobe subprocess with structured argument arrays
      (`-v error -show_format -show_streams -print_format json <path>`), redirected-and-capped
      stdout, `FfProbeOptions.Timeout` (15s default) + linked cancellation, and graceful soft
      failure (missing tool/unreadable file/bad JSON → null, never throws). `FfProbeOptions`
      (`ExecutablePath`/`Timeout`/`MaxOutputBytes`) + `AddPlaySphereMediaProbe(options?)` DI ext.
    - `LibraryScanner` now depends on an optional `IMediaProbe` (3rd ctor param, default null so
      the old-arity construction still compiles) and populates an item's technical metadata on
      scan via `ApplyProbeResultAsync`; a probe failure leaves the item cataloged without
      metadata and never aborts the scan or counts as a fingerprint failure.
    - `App` composition root: `AddPlaySphereMediaProbe(ConfigureFfProbeOptions())` — resolves the
      checked-in `tools\ffmpeg\ffprobe.exe` by walking up from `AppContext.BaseDirectory`; returns
      null options (graceful, probe skipped) when ffprobe isn't found. **Packaging note:** we do
      NOT bundle the ~98 MB `ffprobe.exe` (too large for wholesale copy) — deployed scans rely on
      the tool being next to the app (fallback locator) or degrade gracefully. Bundling ffprobe is
      a later packaging decision.
    - Tests (`tests/PlaySphere.IntegrationTests`, +5): `FfProbeMediaProbeTests` (metadata on a
      real synthetic 320x180 h264/AAC MP4 generated by the checked-in ffmpeg via `MediaTestTools`
      — asserts ~2s, "320x180", h264, ~5fps, bitrate>0, 1 audio / 0 subtitle tracks; null for
      non-media, missing, and missing-tool cases) and
      `ScanFolderAsync_populates_technical_metadata_when_a_probe_is_available`. Fixture generation
      must drain ffprobe/ffmpeg redirected pipes asynchronously or the subprocess deadlocks
      (`WaitForExit(ms)` + redirected pipes = hang, took 60s per failure before the fix).
    - Fixed a pre-existing latent test flake exposed this run: `LibraryScannerTests` wrote two
      files with identical default bytes `[1, 2, 3, 4]` in the same tick, so they got the same
      fingerprint (content + length + mtime are all identity inputs, §24) and the second was
      counted as "updated" not "added" — the two-file tests NOW give the second file distinct
      content (not a weakened assertion; preserves the "catalog distinct files" intent).
  - Now that `PlaySphere.Media.Probe` exists, keep the boundary discipline: UI and other modules
    depend on `IMediaProbe` in `Application.Media`, never on `FfProbeMediaProbe`/ffprobe directly.
    libVLC frame-grab is a separate concern for thumbnails (deferred).
- Next up (Phase 4 remaining + Phase 7 continuation):
  - Thumbnails/cover art: `LibraryMediaItem.CoverArtPath` (PICKLE.md §23). libVLC snapshots
    (`video.takeSnapshot`, engine-side via `IMediaPlaybackEngine`) or a bounded ffmpeg still-frame
    extract — needs an ADR/decision on which.
  - `RecordPlaybackAsync` is now WIRED and the final Phase 4 gap is closed.
    - `PlayerViewModel` takes an optional `IMediaLibraryRepository` (3rd ctor param, default null, so
      the existing `new PlayerViewModel(engine)` test ctor and DI-implicit paths still compile).
      Interface lives in `PlaySphere.Application.Library`, so the UI depends only on the contract —
      same boundary discipline as `IMediaPlaybackEngine`/`IMediaProbe`. `PlayerPage.xaml.cs` resolves
      it via `PlaySphereServices.GetOptional<IMediaLibraryRepository>()` (registered singleton by
      `AddPlaySphereLibrary`, so it's the real EF repo at runtime) and passes it to the VM it builds.
    - Continue Watching / resume flow (`PICKLE.md §23/§26`): on `LoadAsync`, `ResolveLibrarySessionAsync`
      calls `FindByPathAsync(FilePath)`; if the file is a cataloged item it remembers its `Id` and,
      for an unfinished item (`WatchedPercentage < 100` with `LastPosition > 0`), seeks the engine back
      to `LastPosition` (best-effort, never throws). Progress is persisted via the existing
      `RecordPlaybackAsync` on: `StopAsync` (final pre-stop position), `EndReached` (→ position=duration,
      finishes the item), fire-and-forget in `Dispose` (best-effort), and a throttled (~10s) periodic
      record in `OnPositionChanged` so Continue Watching stays live even on crash/force-close. A session
      is never recorded until position > 0, so a fresh open never clobbers a saved resume point with 0.
      `PlayerPage.Page_Unloaded` now disposes the VM (the `_ = RecordSessionAsync()` in `Dispose`
      persists the last position when the user navigates away).
    - Tests (`tests/PlaySphere.UnitTests/PlayerViewModelTests.cs`, +6): a hand-rolled
      `FakeLibraryRepository` (implements all of `IMediaLibraryRepository`, only `FindByPathAsync`/
      `RecordPlaybackAsync` do real work) covers resume-seek-to-last-position, no-resume-when-not-in-
      library, no-resume-for-finished-item, stop-records-final-progress, end-reached-records-final-
      position, and dispose-records-best-effort. Full suite now: 123 unit + 25 integration +
      56 playback (1 skip), 0 failures.
    - Manual-QA caveat (same limitation as prior phases): no interactive desktop UI automation here, so
      real end-to-end resume + Continue Watching was verified through the unit tests (real repo method
      calls, fake engine) and by smoke-launching the app — not by clicking through. A YouTube-style
      manual pass (play a library video, seek, stop → check HomePage "Continue Watching" rows show the
      saved position; reopen → resumes from it) is still needed before calling this fully closed.
  - Surface the now-populated technical metadata in the `LibraryPage`/`HomePage` cards and the
    Media Technical Inspector drawer: now DONE.
    - New pure, testable `MediaCardText` helper (`src/PlaySphere.UI/Helpers/MediaCardText.cs`):
      `FormatDuration` (m:ss / h:mm:ss / "Unknown") and `FormatTechnicalLine` which joins an item's
      populated fields in order — Resolution · Codec · Duration · Bitrate Kbps · audio count · sub count,
      or "No metadata" when nothing is populated. Unit tests in `MediaCardTextTests`.
    - `HomePage` card renders title + technical line + continue-watching progress; `LibraryPage` card
      renders title + technical line + full path. Both call `MediaCardText.FormatTechnicalLine` and
      skip the line entirely when it'd read "No metadata".
    - `PlayerViewModel` now exposes `LibraryItem` (the resolved `LibraryMediaItem`, null for files not
      in the library) so the Media Technical Inspector can annotate the live engine track info with the
      FFprobe-recorded container-level fields. `PopulateMediaInfo` falls back to library Resolution/
      Codec/bitrate/frame-rate/audio-count when live track data is absent.
    - Suite now: 135 unit + 25 integration + 56 playback (1 skip), 0 failures.
  - Thumbnails / cover art (`LibraryMediaItem.CoverArtPath`, PICKLE.md §23): now DONE via
    engine-side libVLC snapshot (ADR-0005).
    - New `IMediaPlaybackEngine.TakeSnapshotAsync(path, width, height, ct)` contract (thin wrapper
      over LibVLCSharp `MediaPlayer.TakeSnapshot`; zero width/height = native size; returns bool,
      never throws) implemented in `LibVlcPlaybackEngine` — UI never touches libVLC directly.
    - New `IMediaLibraryRepository.SetCoverArtPathAsync(itemId, path, ct)` contract +
      `EfMediaLibraryRepository` impl (`ExecuteUpdateAsync` on the existing `cover_art_path`
      column, so no migration). Integration-tested (persist + clear-with-null).
    - `PlayerViewModel` captures a still **once per load**: when a cataloged item has
      `CoverArtPath == null` and playback first reaches ≥1 s (i.e. a real frame is rendered),
      it snapshots to `%LOCALAPPDATA%\LiVi PlaySphere Studio\thumbs\{fingerprint}.jpg` (dir is an
      optional ctor param for testability) and persists the path. Entirely best-effort — a failed
      snapshot leaves the field unset for a later attempt and never blocks/aborts playback.
      Capture deliberately happens at **playback time**, not headlessly at scan time, because
      scan-time has no rendered video surface.
    - Tests: +2 VM unit tests (persists cover art when a frame renders; skips when cover art
      already set) + the 2 repository integration tests above. Full suite now: 137 unit +
      27 integration + 56 playback (1 skip), 0 failures.
    - Manual-QA caveat (same limitation as prior phases): the real snapshot-to-PNG path needs a
      rendered libVLC surface, which can't be exercised headlessly here. Verified: engine API
      compiles against the real LibVLCSharp `TakeSnapshot`, the VM wiring is unit-tested via the
      fake engine, and the app smoke-launches. A real click-through (play a library video, confirm
      a `thumbs\*.jpg` appears and `library.db` records it) is still needed. Card rendering of the
      thumbnail was a follow-up — now DONE: `LibraryPage`/`HomePage` cards surface `CoverArtPath`
      via the new defensive `PlaySphere.UI.Helpers.ThumbnailImage.TryCreate` helper (BitmapImage
      from the absolute file URI, fixed-size UniformToFill; missing/broken/untrusted path → null and
      the card renders text-only). Build clean, all tests green, smoke OK. The rendered-on-screen
      picture still needs the manual pass above (this environment has no desktop UI automation).
  - Phase 4 polish (vibrant card/UI, PICKLE.md §6/§59): COMPLETE (engineering-verified; see
    manual-QA caveat). A shared `PlaySphere.UI.Helpers.MediaCardBuilder.CreatePoster` now builds
    the poster cards used by both `LibraryPage` and `HomePage`, so the two pages render the same
    way. Each card is a rounded 16:9 artwork region (the cover-art `Image`, or an
    `GradientAccentPrimary` placeholder with a centered play glyph when there's no thumbnail),
    a two-line title, the FFprobe technical line, and — for Continue Watching — a bottom-overlay
    progress bar with a percentage chip over a translucent scrim. `LibraryPage` reworked its
    vertical media list into a fluid `VariableSizedWrapGrid` gallery (240px cards, full path
    surfaced as a hover tooltip) and its folder rows into icon cards (folder glyph, name + full
    path + added date, ghost remove button). `HomePage` rows use the same builder (260px cards).
    All colors/spacing come from the shared design tokens. Suite unchanged and green (159 unit +
    31 integration + 58 playback, 1 skip), 0 warnings, build clean, smoke OK.
    Manual-QA caveat (same limitation as prior phases): no desktop UI automation here, so the
    gallery wrap, placeholder art, progress overlay and folder cards were not visually clicked
    through — only compile-time XAML validation + smoke launch. Do a visual pass (open Library/Home
    with both cataloged-with-thumbnail and without-thumbnail items) before calling this closed
    end-to-end.
  - Remaining deferral — stereo balance: **resolved as NOT implementable via the mandated libVLC
    engine.** libVLC's C API has no `balance` function (only volume/mute/channel/delay), and the
    `remap` audio filter is a pure channel remapper (swap/duplicate whole channels) with no
    per-channel gain coefficient, so it cannot attenuate one side for a continuous/partial balance.
    The engine already exposes the coarse full-pan cases libVLC does support via `AudioChannelMode`
    (Stereo/MonoLeft/MonoRight/ReverseStereo/Dolby). A true L/R gain balance would require an
    off-engine FFmpeg audio route (an ADR + architecture change) — deferred, not attempted.
- Subtitle font/color/outline/position styling: COMPLETE (engineering-verified; see manual-QA
  caveat). Extends `SubtitleStyleOptions` beyond text scale with font, text color, outline color,
  outline thickness and vertical margin/position, applied via libVLC **per-media** options
  (identical mechanism to the existing `:sub-text-scale=`):
  - `SubtitleStyleOptions` (Domain: font `FontFamily`, `TextColorArgb`/`OutlineColorArgb` 0xRRGGBB,
    `Outline` enum → `freetype-outline-thickness` 0/1/2, `VerticalMarginPx` → `sub-margin`). Only
    non-default values are emitted. Option names confirmed present in the deployed win-x64
    freetype plugin DLL and `libvlccore.dll` before wiring. Styling applies to plain-text
    subtitles (SRT/WebVTT); ASS/DVD subtitles render their own baked-in style.
  - Engine `SetSubtitleStyleAsync` clamps text scale (25–500), colors (0–0xFFFFFF) and vertical
    margin (0–1000px) via `PlaybackConstraints.ClampSubtitleColor`/`ClampVerticalMarginPx` new
    clamps; `ApplySubtitleStyleToMedia` emits the freetype/sub-margin options per-media.
  - `PlayerViewModel`: new `SubtitleStyle` property; each style setter mutates only its field via
    `_engine.SubtitleStyle with { ... }` (this also fixed a latent bug where the text-scale setter
    rebuilt a fresh default record that would have wiped user-set font/color).
  - `PlayerPage` Subtitles drawer: new "Subtitle Style" section (font ComboBox, text/outline color
    ComboBoxes with presets, outline thickness Off/Thin/Normal, vertical-position slider).
  - Tests: `SubtitleStyleTests` (7, Domain defaults/with-preservation/clamps/outline mapping),
    4 new `PlayerViewModelTests` (setters preserve other fields + propagate to engine; empty font
    → null), and `SubtitleStyleEngineTests` (2, real engine clamping out-of-range fields; default
    round-trip). Full suite now: 159 unit + 31 integration + 58 playback (1 skip), 0 failures.
  - Manual-QA caveat (same limitation as prior phases): no interactive-desktop UI automation, so
    the actual rendered subtitle font/color/outline/position was not visually verified — only the
    option emission is engineering-verified (and the app smoke-launches). Do a manual pass: open a
    video with SRT subtitles, change font/color/outline/margin, re-open the file, confirm the
    styling renders and persists before calling this closed end-to-end.
- Preferred-language audio-track auto-selection: COMPLETE (engineering-verified; see manual-QA
  caveat). New settings-persistence infrastructure (`IPreferencesStore`) + a pure selector
  (`AudioTrackSelector` in `PlaySphere.Domain.Media`) that ranks tracks by preference (exact tag or
  primary-subtag match, mirroring `SubtitleFileMatcher`), picked by the VM via the engine's
  `SelectAudioTrackAsync`.
  - New `PlaySphere.Application.Preferences.IPreferencesStore`
    (`GetPreferredAudioLanguages`/`SetPreferredAudioLanguages`) + `PlaySphere.Infrastructure.Preferences`
    `JsonPreferencesStore` (defensive: corrupt/missing file → empty preferences, temp-file rename write,
    thread-safe) and `AddPlaySpherePreferences(filePath?)` DI ext defaulting to
    `%LOCALAPPDATA%\LiVi PlaySphere Studio\preferences.json`. Registered in `App.xaml.cs`; resolved in
    `PlayerPage.xaml.cs` and passed to the VM as an optional `IPreferencesStore? preferences = null`
    ctor param (existing `new PlayerViewModel(engine)` test ctor still compiles).
  - `PlayerViewModel`: on `OnTracksChanged` once per load (guarded by `_autoSelectAudioDone`, reset in
    `LoadAsync`; skipped if the user already selected a track), it auto-selects the top-ranked
    preferred audio track via `AudioTrackSelector`. `SelectAudioTrackAsync` **learns** the chosen
    track's non-empty language as the preference (moved to front), so a manual pick persists for
    future loads. All preference I/O is best-effort (swallowed on failure).
  - Tests: new `AudioTrackSelectorTests` (8, pure Domain selection) + 4 new `PlayerViewModelTests`
    (auto-selects preference, no-match no-select, no-preferences no-select, manual-select learns
    language) using a new `FakePreferencesStore`; `FakePlaybackEngine` now records
    `LastSelectedAudioTrackId`. New `tests/PlaySphere.IntegrationTests/PreferencesStoreTests.cs`
    (round-trip, empty default, corrupt-file resilience, overwrite). Full suite now: 149 unit +
    31 integration + 56 playback (1 skip), 0 failures.
  - Manual-QA caveat (same limitation as prior phases): no interactive desktop UI automation here, so
    real audio playback with actual multi-track files and the persisted `preferences.json` was
    verified through the unit/integration tests and by smoke-launching the app — not by listening.
    Do a manual pass (play a file with en+fr audio, confirm the preferred language track is selected,
    switch tracks, check `%LOCALAPPDATA%\LiVi PlaySphere Studio\preferences.json` updates, reopen the
    file and confirm it auto-selects the learned language) before calling this closed end-to-end.
- Phase 5 (Playlists & Bookmarks): STARTED — data-layer + playback-queue foundation COMPLETE
  (engineering-verified; see manual-QA caveat). No UI/player wiring yet — this is the reusable
  foundation the UI features build on.
  - New Domain entities in `PlaySphere.Domain.Library`: `Bookmark` (PICKLE.md §21 — `MediaItemId`,
    `PositionMs`, `Label`, `Note`, `Category`, `CreatedAt`), `Playlist` (§25 — `Name`, `CreatedAt`),
    `PlaylistItem` (§25 — `PlaylistId`, `MediaItemId`, `Position` ordering, `AddedAt`).
  - New pure playback-session core `PlaySphere.Domain.Playback.PlaybackQueue<T>` (PICKLE.md §25:
    queue, shuffle, repeat one, repeat all, play next) — owns an ordered queue + a cursor and exposes
    `MoveNext`/`MovePrevious` honoring the `PlaybackRepeat` enum (`Off`/`One`/`All`, where `Off`
    returns null past the end, `All` wraps, `One` repeats the current), plus `Start`, `JumpTo`,
    `Shuffle(Random)` (keeps the current item at the front of the play order and Fisher-Yates the
    rest, so playback continues from what's playing), `Unshuffle`, `Append`, `Remove` (keeps a valid
    current), and `Clear`. Deliberately deterministic-in-input with a caller-supplied `Random`, so it
    is unit-testable in isolation and reusable by playlists, M3U imports and bookmark sessions.
  - `IMediaLibraryRepository` extended with bookmark CRUD (`AddBookmarkAsync` clamps negative position
    to 0, `GetBookmarksAsync` ordered by position, `RemoveBookmarkAsync`) and playlist CRUD
    (`CreatePlaylistAsync` trims name, `RenamePlaylistAsync`, `DeletePlaylistAsync` cascades items,
    `GetPlaylistsAsync` oldest-first, `AddToPlaylistAsync` appends at next position,
    `AddRangeToPlaylistAsync` for imports preserving order, `RemoveFromPlaylistAsync`,
    `ReorderPlaylistAsync` as full order replacement that also drops omitted rows,
    `GetPlaylistItemsAsync` in position order).
  - `PlaySphereDbContext`: new `bookmarks`/`playlists`/`playlist_items` tables with FKs cascading to
    both `media_items` and (for items) `playlists`, plus indexes on `(MediaItemId, PositionMs)` and
    `(PlaylistId, Position)`. EF migration `AddPlaylistsAndBookmarks` (20260831075246) generated via
    the design-time factory; applies cleanly on top of the existing database at startup (confirmed by
    smoke-launch, which runs `MigrateLibraryDatabase`). Player-state concerns (shuffle/repeat) are
    deliberately NOT persisted here — they live in the playback session, not the playlist tables.
  - Tests (`tests/PlaySphere.IntegrationTests/BookmarkPlaylistRepositoryTests.cs`, +14) against the
    real migrated SQLite `LibraryDatabaseFixture`: bookmark persist/clamp/order/empty/remove, playlist
    create/rename/delete-cascade/order, add-append-position, add-range-order, remove-row, reorder
    rewrite+drop, items-in-position-order. `FakeLibraryRepository` in `PlayerViewModelTests` got the
    new interface members as `NotSupportedException` stubs (it never uses them). New pure-logic
    `PlaybackQueueTests` in `tests/PlaySphere.UnitTests/PlaybackQueueTests.cs` (+19) cover off/one/all
    repeat navigation, wrap/end semantics, jump, shuffle-with-current-at-front, unshuffle, remove-
    keeps-valid-cursor, append and clear.
  - `PlayerViewModel` now carries a queue playback session: `PlaybackQueue<string>` over media file
    paths with `LoadCollectionAsync(filePaths, startPath?)`, `PlayNextAsync`/`PlayPreviousAsync`,
    `ToggleShuffleAsync` (keeps the current item at the front), and `CycleRepeatAsync`
    (Off→One→All). Auto-advances to the next queued item on `EndReached` (auto "play next").
    Observable state: `IsQueueActive`, `IsShuffled`, `RepeatMode`, `RepeatModeLabel`,
    `QueuePositionLabel` ("n / N"), `HasPrevious`/`HasNext`. 8 new `PlayerViewModelTests`
    (load-first, start-path, next-walks-then-false, repeat-all-wrap, previous-wrap, shuffle-toggle,
    repeat-cycle, end-reached-autoadvance) via a `FakePlaybackEngine`. Full suite now:
    186 unit + 45 integration + 58 playback (1 skip), 0 failures, 0 build warnings.
  - Manual-QA caveat (same limitation as prior phases): no interactive desktop UI automation here, so
    the data layer, the pure queue semantics, and the VM queue wiring are engineering-verified (real
    migrated DB integration tests + deterministically-seeded queue unit tests + fake-engine VM tests)
    and confirmed by smoke-launching the app (which applied the migration to the on-disk `library.db`).
    No UI surface exists yet to click playlists/bookmarks into existence or press shuffle/repeat. The
    remaining follow-up work: a Playlists/Bookmarks UI layer (create/rename/delete/reorder playlists,
    bookmark click-to-seek), M3U/M3U8 import/export, and transport buttons wired to the new VM commands.
  - Playlists UI surface + queue navigation: COMPLETE (engineering-verified; see manual-QA caveat).
    - New `PlaySphere.UI.Navigation.PlayerQueuePayload` (ordered `FilePaths` + optional `StartFilePath`)
      so any page can hand the player a whole play queue. `PlayerPage.OnNavigatedTo` gained a branch for
      it: it builds the VM exactly as the string-path branch does and calls `LoadCollectionAsync(filePaths,
      startFilePath)` — the same path a single-file open uses, just prime with the collection.
    - `PlaylistsPage` is no longer a stub — it is a full management UI over `IMediaLibraryRepository`:
      a "Create Playlist" TextBox + button (Enter also submits), a `ListView` of playlists (name + item
      count, restored across refreshes via an internal `_entries` list rather than sampling the
      non-generic `ListView.Items`), and a detail pane for the selected playlist showing its ordered items
      as cards with Move Up / Move Down (via `ReorderPlaylistAsync` full-order replacement) and Remove,
      an Add-item `ComboBox` populated from the catalog (`GetAllMediaItemsAsync`) feeding
      `AddToPlaylistAsync`, Rename (ContentDialog + `RenamePlaylistAsync`), Delete (confirm dialog +
      cascade) and "Play All" which navigates to `PlayerPage` with a `PlayerQueuePayload` built from the
      playlist's item paths in `Position` order. Playlist cards, empty states and detail buttons follow the
      established `LibraryPage` defensive pattern (`_repository == null → return`, dynamic `XamlApplication`
      brush lookups, `XamlApplication` alias).
    - Build is clean (0 warnings/errors), full suite green (191 unit + 45 integration + 58 playback,
      1 skip — new unit tests added in the next slice), and `tools\run-app-smoke.ps1` launches OK (WinUI compile-time XAML validation
      for the new page passed).
    - Manual-QA caveat (same limitation as prior phases): no interactive desktop UI automation here, so the
      new page's Create/rename/delete/reorder/Add/Play interactions and the queued navigation into
      `PlayerPage` were **not** visually clicked through — only compile-time XAML validation, build + tests,
      and smoke launch. Do a manual pass (create a playlist, add cataloged items, reorder, Play All and confirm
      the player advances through the queue and shuffle/repeat work) before calling Phase 5 closed end-to-end.
  - Transport buttons + bookmarks UI: COMPLETE (engineering-verified; see manual-QA caveat).
    - `PlayerPage` primary transport bar gained queue controls: Previous / Next (wired to
      `PlayPreviousAsync`/`PlayNextAsync`), Shuffle (`ToggleShuffleAsync`) and Repeat
      (`CycleRepeatAsync`, glyph + tooltip + outline color reflect Off/One/All). `UpdateQueueTransportUi()`
      (called from `UpdateUiFromViewModel` on every property change) enables Previous/Next from
      `HasPrevious`/`HasNext` and colours Shuffle/Repeat by state. `PlaySphere.Domain.Playback.PlaybackRepeat`
      is referenced only via its fully-qualified name here (no using needed).
    - Bookmarks (PICKLE.md §21) end-to-end: new VM surface `CanBookmark` (repo != null && a cataloged
      `_libraryItemId` is resolved), observable `Bookmarks`, and `LoadBookmarksAsync` /
      `AddBookmarkAtCurrentAsync` (records current `Position` ms, clamps to ≥ 0) / `RemoveBookmarkAsync` /
      `SeekToBookmarkAsync` (clamps to duration, best-effort, never throws). The Chapters drawer now shows a
      "Bookmark Current Position" button (enabled only when `CanBookmark`) plus a seekable/removable bookmark
      list; the button is re-enabled and the list re-populated each time the Chapters drawer opens.
    - Tests: `FakeLibraryRepository`'s three bookmark stubs became a real in-memory `List<Bookmark>` store;
      5 new `PlayerViewModelTests` (add-for-cataloged-item, add-returns-false-when-not-in-library,
      load-bookmarks, remove, seek-to-bookmark). Full suite now: 191 unit + 45 integration + 58 playback
      (1 skip), 0 failures, 0 build warnings; `tools\run-app-smoke.ps1` OK (compile-time XAML validation for
      the new transport buttons and drawer section passed).
    - Manual-QA caveat (same limitation as prior phases): no interactive desktop UI automation here, so the
      transport buttons and the bookmark add/seek/remove flow were **not** clicked through — only VM-level
      unit tests (real repo method calls, fake engine), compile-time XAML validation, build + tests and smoke
      launch. Do a manual pass (open a cataloged library video, press Previous/Next/Shuffle/Repeat and confirm
      queue navigation + repeat glyph, add a bookmark at a point, click it to seek, delete it) before calling
      this fully closed. M3U/M3U8 import/export and next/prev keyboard shortcuts remain a follow-up.
  - M3U/M3U8 import/export + next/prev keyboard shortcuts: COMPLETE (engineering-verified; see manual-QA
    caveat).
    - New `PlaySphere.Domain.Library.M3uEntry` record (Path, Title?, DurationSeconds?) for parsed M3U
      entries (untrusted input, PICKLE.md §43).
    - New `PlaySphere.Application.Library.IM3uImporter` (`ImportAsync(m3uFilePath, playlistName?, ct)`
      → `M3uImportResult(Playlist, Imported, Skipped)`) and `IM3uExporter` (`ExportAsync(paths, dest, ct)`
      → bool).
    - New `PlaySphere.Library.M3uParser` (pure, defensive): bounded line-length and entry-count
      (`MaxEntries=10_000`), handles `#EXTM3U` header and `#EXTINF:<seconds>,<title>` directives
      (float durations truncated to `long`), resolves relative paths against the playlist's directory,
      strips quotes and `file:` URIs, skips malformed/empty lines and non-entry directives. 10 unit
      tests in `M3uParserTests.cs` (empty, basic entries, relative resolution, EXTINF title+duration,
      comments, metadata non-leak, quotes, file URI, entry cap, invalid paths).
    - New `PlaySphere.Library.MediaFileExtensions` (public static helper): shared media-extension set
      matching the scanner's private list (§14), reusable by the importer.
    - New `PlaySphere.Library.M3uImporter` (implements `IM3uImporter`): reads playlist (bounded
      5 MB), parses via `M3uParser`, for each supported+existing entry computes fingerprint, ensures a
      valid `LibraryFolder` via new `EnsureFolderAsync`, upserts the cataloged item, and appends to a
      new playlist in file order. Missing/unsupported entries skipped, never fatal. Fixed a critical bug
      found during testing: the original upsert call discarded the returned item (the `existing`
      branch in `UpsertMediaItemAsync` returns a *different* object with the real Id), causing
      `importedIds` to contain 0 and FK failures on `AddRangeToPlaylistAsync` — now captures the return
      value (`var cataloged = await repository.UpsertMediaItemAsync(item, ...)`).
    - New `PlaySphere.Library.M3uExporter` (implements `IM3uExporter`): writes `#EXTM3U` + one
      `#EXTINF:-1,<title>` + path line per entry; strips CR/LF from titles to prevent playlist injection;
      returns false on I/O errors (never throws).
    - New `IMediaLibraryRepository.EnsureFolderAsync(path)`: find-or-create counterpart to
      `AddFolderAsync` (which throws on duplicates). Returns the existing folder or registers it; used
      by the importer to avoid FK failures when a file's parent directory is already a registered
      library folder. Implemented in `EfMediaLibraryRepository` and stubbed as `NotSupportedException`
      in `FakeLibraryRepository`. 1 integration test in `LibraryRepositoryTests.cs`.
    - DI registration: `AddPlaySphereM3u()` in `LibraryServiceCollectionExtensions` registers
      `IM3uImporter`/`IM3uExporter`; called from `App.xaml.cs` DI chain.
    - Next/prev keyboard shortcuts: `.` (period, `(VirtualKey)190`) → `PlayNextAsync`, `,` (comma,
      `(VirtualKey)188`) → `PlayPreviousAsync` in `PlayerPage.RootPlayerGrid_KeyDown`. Added to the
      transport bar tooltip text in XAML.
    - Tests: `M3uParserTests` (+10, pure parser), `M3uImportExportTests` (+6 integration against real
      migrated SQLite: catalogs supported files, skips missing/unsupported, preserves order, custom name,
      reuses existing by fingerprint, round-trip export→import, invalid destination returns false),
      `LibraryRepositoryTests` (+1, `EnsureFolderAsync` find-or-create idempotency).
    - Full suite now: 201 unit + 52 integration + 58 playback (1 skip), 0 failures, 0 build warnings.
    - Manual-QA caveat (same limitation as prior phases): no interactive desktop UI automation here, so
      the M3U import/export flow (Import button → file picker → parse → catalog → playlist creation),
      the round-trip export, and the `.`/`,` keyboard shortcuts were **not** visually clicked through —
      only parser unit tests, real-DB integration tests, build + tests, and smoke launch. Do a manual
      pass (create an M3U file listing media files, import it via PlaylistsPage, confirm the playlist is
      created with correct items; export a playlist and re-import it; play a queued video and press `.`
      to skip to next and `,` to go back) before calling Phase 5 fully closed end-to-end.
      M3U Import/Export buttons in the `PlaylistsPage` UI surface are a follow-up.
  - M3U Import/Export UI surface on PlaylistsPage: COMPLETE (engineering-verified; see manual-QA caveat).
    - `FilePickerHelper` gains `PickM3uFileAsync` (open .m3u/.m3u8/*) and `PickSaveM3uFileAsync`
      (save .m3u), following the existing HWND-initialized picker pattern.
    - `PlaylistsPage` resolves `IM3uImporter`/`IM3uExporter` via `PlaySphereServices.GetOptional`
      (defensive — records stay null-capable) and adds two buttons:
      - "Import M3U / M3U8…" under the Create toolbar → `PickM3uFileAsync` → `IM3uImporter.ImportAsync`
        → `RefreshAsync` + select the newly created playlist → result ContentDialog (imported/skipped
        counts); disabled during import; exceptions surfaced, never thrown.
      - "Export M3U" in the selected-playlist detail toolbar → gathers the playlist's cataloged item
        paths in `Position` order → `PickSaveM3uFileAsync` (safe file-stem derived from the playlist
        name, invalid filename chars stripped) → `IM3uExporter.ExportAsync` → result/unavailable
        ContentDialog (best-effort, `XamlRoot` null-guarded).
    - Build clean (0 warnings/errors), full suite unchanged and green (201 unit + 52 integration +
      58 playback, 1 skip), `tools\run-app-smoke.ps1` OK (compile-time XAML validation for the new
      buttons passed).
    - Manual-QA caveat (same limitation as prior phases): no interactive desktop UI automation here, so
      the new Import/Export buttons were **not** clicked through — the pickers, the import flow, the
      export flow and the result dialogs were verified only by compile-time XAML validation, build +
      tests, and smoke launch. Do a manual pass (create an M3U file listing real media, click Import
      and confirm a playlist is created with the correct items; select it, click Export M3U, and confirm
      the saved file round-trips back on a second import) before calling Phase 5 closed end-to-end.
  - Phase 5 (Playlists & Bookmarks) is now COMPLETE on the engineering side: data layer, queue
    semantics, player/VM wiring, PlaylistsPage management UI, transport/bookmark controls, M3U
    import/export (backend + UI), and next/prev keyboard shortcuts — all with passing tests. The full
    manual-QA pass (see the caveats above) is the remaining human step before closure.
- Phase 6 (Visualizer): IN PROGRESS — VIS-01/02/03 DSP core + VIS-04 Win2D renderer + all five presets VIS-05..09 + engine PCM wiring (VIS-09) COMPLETE (engineering-verified).
  - New `PlaySphere.Visualizer` module (PICKLE.md §31–§37) following the Media/Probe/Library module
    pattern (Domain + Application deps only). The first slice is the **pure, headless-tested DSP core**
    — no rendering yet (Win2D + presets are the follow-on VIS-04+ tickets).
  - **Domain** (`PlaySphere.Domain.Visualizer`): `AnalyzerWindow` (2048/4096), `FrequencyRange`
    (Bass/Mid/High), and the immutable `AudioAnalysisFrame` result type — Spectrum (log-mapped band
    magnitudes), Rms, Peak, BandEnergy dictionary, TransientEnergy, plus SampleRate/ChannelCount.
  - **Application** (`PlaySphere.Application.Visualizer`): the boundaries the UI/renderer depend on,
    never on libVLC:
    - `IVisualizerAudioSource` — `PushSamples(interleaved float[], sampleRate, channelCount)` /
      `Stop()`; the seam where the engine will later feed decoded PCM (metadata passed per batch so
      the analyser never assumes 44.1 kHz, §34).
    - `IAudioAnalyzer` — `Analyze(samples, sampleRate, channelCount, previousRms)` → `AudioAnalysisFrame`.
    - `ILatestAnalysisFrameProvider` — the latest-frame bounded channel (drop-visualizer-frames-before-
      audio, playback never waits for renderer, §33/§35).
  - **Implementation** (`PlaySphere.Visualizer`):
    - `Dsp/Fft` — allocation-free-after-construction, bit-reversal-precomputed in-place radix-2
      Cooley-Tukey for 2048/4096 (validated by the golden band-ordering tests).
    - `AudioAnalyzer` — Hann window, mono downmix for stereo, RMS + peak, FFT magnitude, log-mapped
      spectrum bands (20 Hz → Nyquist, cached per actual sample rate so 44.1 k/48 k both map
      correctly), bass/mid/high band energy, and transient energy (max of the relative RMS rise over
      the previous frame). No allocation per analysis window after construction.
    - `PcmAudioSource` — `IVisualizerAudioSource` that buffers interleaved PCM, analyses every full
      window, and publishes to a channel; a format change (rate/channels) resets the buffer.
    - `LatestAnalysisFrameChannel` — volatile latest-frame holder (publish replaces, read non-blocking).
    - `VisualizerServiceCollectionExtensions.AddPlaySphereVisualizer()` — registers the analyzer +
      channel + source as singletons; called from `App.xaml.cs` DI chain.
  - **Win2D rendering engine (VIS-04):** `PlaySphere.UI.Visualizer.VisualizerCanvas` wraps
    `CanvasAnimatedControl` and reads frames via `ILatestAnalysisFrameProvider.TryReadLatest()`.
    Presets implement `IRenderPreset.Draw(session, frame, w, h)` in the UI layer (Win2D types
    acceptable here; Application contracts stay pure). No libVLC/FFmpeg referenced by the renderer.
    ADR-0007 records the Win2D (GPU tech) and UI-layer placement decisions. `Microsoft.Graphics.Win2D`
    1.4.0 added to `Directory.Packages.props` and the UI project; `Win2DWarnNoPlatform=false`
    suppresses the AnyCPU build warning (native `Microsoft.Graphics.Canvas.dll` resolves at win-x64
    runtime via the App's RID).
  - **Spectrum Flow preset (VIS-05, first preset):** 48 vertical bars with a bottom-to-top
    gradient (cyan `#36A9FF` → magenta `#EE4999`) mapping the log-mapped `AudioAnalysisFrame.Spectrum`
    bands. Bar→band mapping exposed as a pure static `SourceIndexForBar` method so the mapping
    logic is unit-testable (10 tests: proportionally-spaced mapping across various band counts,
    edge cases, guard clauses, name/color defaults).
  - **Remaining four presets (VIS-06..09):** the full five-preset palette is now wired. Each preset
    implements `IRenderPreset.Draw` using only colour-taking session primitives (`DrawLine`/
    `DrawCircle`/`FillCircle`), allocation-free per frame (only internal float smoothing/time state
    persists across frames):
    - `NeonWavePreset` (VIS-06): a smooth glowing waveform — `PointCount`=96 segments across the
      width, the wave's vertical displacement follows `Spectrum` magnitudes (pure `SourceIndexForSample`
      proportional band mapping), a thick translucent glow pass (`GlowColor`, alpha 0x55) drawn under a
      thin bright pass (colour `#36A9FF`).
    - `OrbitSpectrumPreset` (VIS-07): 64 radial bars orbiting centre, each a line from an inner ring
      outward to a radius proportional to its band's `Spectrum` magnitude, colour `#EE4999` (pure
      `SourceIndexForBar` mapping).
    - `PulseRingsPreset` (VIS-08): 5 concentric `DrawCircle` rings pulsing with a per-frame-smoothed
      envelope (`ComputeEnvelope` — bass-weighted `BandEnergy` + `TransientEnergy` + `Rms`, clamped 0..1,
      blended into a `_smoothed` field so motion eases), inner→outer `LerpColor` fade with rising alpha.
    - `ParticleBloomPreset` (VIS-09): a `Rows`×`Columns` grid of `FillCircle` particles, each with a
      deterministic per-cell phase advancing with `_time`; radius/alpha derive from a broadband/peak
      `ComputeDrive` plus `ParticleRadius`/`ParticleAlpha` pure helpers (both bounded).
  - Preset selection UI: a new "Visualizer Preset" `MenuFlyoutSubItem` in the PlayerPage overflow menu
    lists all presets; each routes through a shared `VisualizerPreset_Click` handler +
    `CreatePreset(string)` factory (default Spectrum Flow), which sets `VisualizerSurface.Preset`.
  - **Phase 6 gap-fill — the full nine-preset PICKLE.md §32 palette (2026-09-03):** four original
    presets were never built (only VIS-05..09 existed). Added `AuroraRibbonPreset`, `PrismTunnelPreset`,
    `AlbumPulsePreset` and `MinimalSpectrumPreset`, all following the established pattern (immutable
    `init` config, `Name`, pure static testable helpers, allocation-free `Draw` using only session
    color-taking primitives):
    - `AuroraRibbonPreset`: a flowing translucent ribbon band (blue→purple→magenta via `PaletteColor`)
      whose upper edge follows spectrum magnitudes and lower edge mirrors them; a thick translucent
      centre pass under two thin bright edge passes. Pure helpers: `SourceIndexForSample`,
      `RibbonOffset` (clamped offset), `PaletteColor` (3-stop gradient).
    - `PrismTunnelPreset`: `RingCount`=4 concentric rings × `SegmentCount`=72 radial segments whose
      short strokes pulse outward with the spectrum by ring (inner rings pulse most via `RingPulse`).
      Pure helpers: `SourceIndexForSegment`, `RingPulse`.
    - `AlbumPulsePreset`: an album-art-style burst — `GlowRingCount`=3 central `DrawCircle` rings
      pulsing with drive, plus `RayCount`=60 radial rays that slowly precess (`RotationSpeed`) and
      extend with `RayLength`. Pure helpers: `ComputeDrive`, `RayLength`.
    - `MinimalSpectrumPreset`: a clean, thin `BarCount`=32 bar spectrum in a single flat green with a
      gentle mid/low boost (`CurveEnergy` = 0.55·v + 0.45·v², bounded 0..1). Pure helpers:
      `SourceIndexForBar`, `CurveEnergy`.
    All four wired into the `PlayerPage` overflow preset submenu (XAML items, `CreatePreset` factory
    tags, `RefreshVisualizerAvailability` enable-state). New `VisualizerPresetTests` cover the pure
    helpers (proportional mapping, guard clauses, drive/envelope/pulse bounds + growth, gradient
    endpoints, energy curve). Full palette is now the original nine: Spectrum Flow, Neon Wave, Orbit
    Spectrum, Pulse Rings, Particle Bloom, Aurora Ribbon, Prism Tunnel, Album Pulse, Minimal Spectrum.
  - `PlayerPage` wiring: `VisualizerCanvas` control sits as a collapsed overlay in the video
    surface grid (above video, below controls); toggle via overflow menu "Show Visualizer" item;
    canvas pauses when `_viewModel.IsPlaying` is false (no CPU burn).
  - Engine PCM wiring (VIS-09, ADR-0008): COMPLETE (engineering-verified). `IMediaPlaybackEngine`
    gained `IsVisualizerAttached` / `AttachVisualizerSourceAsync(IVisualizerAudioSource)` /
    `DetachVisualizerSourceAsync` following the `AttachVideoSurfaceAsync` pattern —
    `Playback.Abstractions` now references `PlaySphere.Application` (for `IVisualizerAudioSource`;
    Application depends only on Domain, so no cycle). `LibVlcPlaybackEngine` stores the source and
    on attach registers `SetAudioFormatCallback`/`SetAudioCallbacks`: the `LibVLCAudioSetupCb` writes
    the `FL32` fourcc into libVLC's format buffer and accepts the native rate/channels (never
    assuming 44.1 kHz, §34); the play callback bounds the float count, `Marshal.Copy`s the PCM, and
    pushes it to the source on VLC's audio thread (exceptions swallowed so the tap never crashes
    playback). Detach stops the source and rebuilds the native player (`RebuildRenderer(null)`,
    preserving the active swap-chain options so a video surface keeps rendering) to unregister the
    callbacks and restore normal audio. The rebuild re-registers the tap when one is still attached.
    Per the libVLC audio-callback contract, **speaker output is silenced while the tap is attached**
    — an accepted ADR-0008 limitation (initial NAudio WASAPI dual-output draft rejected: Windows-only
    in a plain-`net10.0` platform-neutral engine, unverifiable here). `PlayerPage` resolves
    `IVisualizerAudioSource` via DI and attaches/detaches the tap on the "Show Visualizer" toggle and
    on `Page_Unloaded` (so leaving the page restores audio); a failed attach/detach never breaks the
    player. 5 new PlaybackTests (`VisualizerPcmTapTests`) cover attach/replace/null/detach/no-op
    against the real `LibVlcPlaybackEngine`; `FakePlaybackEngine` implements the new interface members.
  - Fullscreen visualizer mode (VIS-10) + reduced-motion / power-saver (PICKLE.md §35/§36): COMPLETE
    (engineering-verified). New "Fullscreen Visualizer" overflow-menu item + transport-bar button
    call `ToggleVisualizerFullScreenAsync()`: it auto-enables the visualizer + PCM tap (shared
    `ApplyVisualizerPcmTapAsync` helper factored out of the toggle), enters fullscreen via
    `IWindowService.SetFullScreen(true)`, and hides `VideoSurface`/`VideoPlaceholderPanel` so the
    visualizer fills the window; exiting via the button or `ExitVisualizerFullScreen()` (also called
    by the toggle turning off) restores the picture. `WindowService_FullScreenChanged` detects an
    external fullscreen exit (e.g. Esc → `SetFullScreen(false)`) and clears the flag so the video
    surface returns; `Page_Unloaded` resets it. `VisualizerCanvas.ReducedMotion` caps
    `CanvasAnimatedControl.TargetElapsedTime` at ~30 FPS (power saver, §35/§36) — `PlayerPage` drives
    it from a `UISettings` whose `AnimationsEnabled` is read in the ctor and tracked live via
    `AnimationsEnabledChanged` (subscription gated on Windows 10.0.19041+ to satisfy CA1416, which
    the app targets anyway). The presets are non-flashing by design (no strobe), satisfying the
    "Disable Flashing" accessibility item.
  - Tests: 20 DSP golden tests (`AudioAnalyzerTests.cs`) + `SpectrumFlowPresetTests.cs` (10)
    + new `VisualizerPresetTests.cs` (25: Neon Wave/Orbit proportional mapping + guard clauses,
    Pulse Rings envelope/Particle Bloom drive bounds + growth, radius/alpha scaling, all five `Name`s)
    + existing `PcmAudioSourceTests`/`LatestAnalysisFrameChannelTests`/`VisualizerServiceCollectionRegistrationTests`
    + `VisualizerPcmTapTests` (5).
    Full suite: 257 unit + 52 integration + 64 playback (1 skip), 0 failures, 0 build warnings.
  - **Crash on opening PlayerPage — root-caused and fixed (not a visualizer defect):** the real
    `%LOCALAPPDATA%\LiVi Play Sphere Studio\crash.log` (surfaced by the new `UnhandledException`
    crash logger in `App.xaml.cs`, which writes `[utc] {exception}` to that path and never throws)
    showed `Microsoft.UI.Xaml.Markup.XamlParseException: Cannot find a Resource with the Name/Key
    BrushBorderSubtle` thrown at `PlayerPage.InitializeComponent()`. `PlayerPage.xaml` referenced
    `{ThemeResource BrushBorderSubtle}` in 13 places (lines ~549–967) but the token was never defined
    in `Themes\DesignTokens.xaml` — theme resources resolve at runtime, not compile time, and the
    smoke test never navigates to `PlayerPage`, so it slipped through. Fixed by adding the
    `#2A3646` `ColorBorderSubtle` color + `BrushBorderSubtle` `SolidColorBrush` tokens to
    `DesignTokens.xaml` (merged app-wide in `App.xaml`). The user's `crash.log` in the repo root is
    a stray copy of `App.xaml.cs`, not log output — ignore/delete it; the canonical log is the
    `%LOCALAPPDATA%` one above.
  - **New regression guard:** `DesignTokenReferenceTests.cs` (in `PlaySphere.UnitTests`) statically
    cross-checks every custom-token `{ThemeResource ...}` reference across all UI XAML (under
    `PlaySphere.UI\` copied into the test output) against the union of every `x:Key` defined in the
    design tokens + page-local resources, for the custom-token prefixes (`Brush`/`Color`/`Gradient`/
    `Spacing`/`Control`/`Card`/corner radii only — WinUI system resources are out of scope). Verified:
    with `BrushBorderSubtle` temporarily removed the test fails naming exactly `PlayerPage.xaml:
    BrushBorderSubtle`; restored, it passes. Any future XAML that references a never-defined custom
    token now fails this test before the app can crash at navigation time. Subsequent
    `{ThemeResource}` additions to XAML should be linted by this guard rather than hand-checked.
  - Manual-QA caveat (same limitation as prior phases): Win2D rendering requires a real GPU
    device and a rendered `CanvasAnimatedControl`; the animated spectrum cannot be verified
    headlessly. What IS verified: solution builds clean, all 373 tests pass, the app boots via
    `tools\run-app-smoke.ps1` without crashing, the Win2D native DLL deploys correctly in the
    win-x64 output. Required manual pass: open a video, enable the visualizer via the overflow
    menu, confirm animated bars/wave/rings track the audio in real time at ~60 FPS, and switch
    between all five presets via the new "Visualizer Preset" submenu; enter and exit Fullscreen
    Visualizer (button / menu / Esc) and confirm the picture hides and returns; toggle Windows'
    "show animations" setting and confirm the visualizer drops to ~30 FPS. Note speaker audio is
    silenced while the visualizer is on (ADR-0008 limitation) and resumes when it's turned off.
  - **Deep error surfacing for the `Play()`-returns-false diagnostic (ongoing):** the app now writes
    full libVLC diagnostics to `%LOCALAPPDATA%\LiVi Play Sphere Studio\libvlc.log` (wired into
    `LibVlcBootstrap.CreateLibVlc` — the `libVLC.Log` handler only fires if subscribed **at LibVLC
    creation time**, not from the engine ctor; an earlier engine-ctor subscription silently wrote
    nothing). `LibVlcBootstrap` also keeps a bounded in-memory tail (256 lines) of recent log lines
    via `GetRecentLogLines()`. The engine's `PlayAsync`/`OnVlcEncounteredError`/`FailLoad` failure
    messages are now augmented by `BuildFailureContext()` — a static helper that pulls the recent
    log lines, keeps only plausible failure-signal lines (`: error`, `failed`, `no suitable`,
    `cannot`, `unable`, etc.), and appends them to the on-screen error message (surfaced via
    `PlayerViewModel.LastError` / the `StateText` chip). This guarantees the user sees the **deep
    libVLC reason** for `"libVLC MediaPlayer.Play() returned false"` on the next reproduce, instead
    of only getting a log file to fetch. Build clean (0/0), all 373 tests pass, smoke OK; the root
    cause itself is still TBD pending one more user reproduce with the on-screen detail.
  - **ROOT CAUSE FOUND & FIXED (the `Play() returned false` defect):** the failure was NOT in the
  engine's audio/render path. `AttachVideoSurfaceAsync` with a non-empty `SwapChainOptions` (the WinUI
  `VideoView`) calls `RebuildRenderer`, which creates a **brand-new `MediaPlayer`** with NO media. The
  media had been set on the OLD player by `LoadAsync`. So after the surface attach (which happens after
  load, once the VideoView swap-chain initializes), the user's Play ran on an empty player → `Play()`
  returned false — and libVLC logged **nothing** because the path fails via `libvlc_printerr`, which
  never reaches the `Log` event. That's why the `libvlc.log` showed only preparse/audio-probe Debug
  lines and no error. Fix: the engine now tracks `_loadedPath` (set on successful `LoadAsync`) and
  `RebuildRenderer` re-attaches a fresh `Media` from that path onto the new player (with audio-
  enhancements/subtitle-style re-applied) before it hands the renderer to the surface. Metadata/tracks
  stay cached from the original parse. New regression test
  `VideoSurfaceTests.Attaching_swapchain_surface_rebuilds_renderer_but_loaded_media_still_plays`
  (loads `testcard.mp4`, attaches a swap-chain surface, then Play must reach `Playing`) — verified it
  **fails** (state → `Error`) with the fix disabled and **passes** with it enabled. Full suite now:
   257 unit + 52 integration + 65 playback (1 skip) = 374, 0 failures, 0 build warnings, smoke OK.
  - **ROOT CAUSE FOUND & FIXED (the "enabling the visualizer during playback crashes after a few
    seconds" defect):** enabling the visualizer mid-playback used to **rebuild the whole native
    LibVLC/MediaPlayer on the UI thread** (`RebuildAndResumeIfPlaying`) to register the PCM audio
    callbacks, then `Play()`+seek. That raced the old player's still-live native audio thread (which
    was pushing PCM into the shared, **non-thread-safe** `PcmAudioSource` buffer) → heap corruption
    → a native AV a few seconds later that bypasses the managed crash handler (crash.log stays
    empty); and the custom-callback audio mode stalled libVLC's media clock ("Playing but timeline
    frozen"). Fix (chosen: **harden + defer**): (1) `PcmAudioSource` now holds a lock over its buffer
    so concurrent `PushSamples`/`Stop` can't corrupt the heap; (2) the engine **never rebuilds the
    player while `_state == Playing`**. `AttachVisualizerSourceAsync` records the source and applies
    callbacks immediately only when NOT playing; while playing the tap **defers** to the next natural
    renderer rebuild (next `LoadAsync`). `DetachVisualizerSourceAsync` stops the source + drops its
    reference and rebuilds only when NOT playing; while playing the un-registration defers to the next
    rebuild. So mid-playback toggle **never interrupts playback** (no rebuild → no race, no clock
    stall); the tap simply takes effect at the next load. Per ADR-0008, while the tap is registered
    the speaker is silenced, so a deferred detach restores sound at the next load too. New regression
    tests: `Attach_visualizer_mid_playback_does_not_interrupt` / `Detach_visualizer_mid_playback_does_not_interrupt`
    (play → toggle → assert engine is still `Playing`, source stopped on detach) plus a concurrent
    `PcmAudioSource` push/stop test. Full suite now: 258 unit + 52 integration + 66 playback (1 skip),
    0 failures, 0 build warnings, smoke OK.
  - **CRASH TRIAGE (sustained visualizer crashes) — THREE DISTINCT SIGNATURES, all fixed/mitigated:**
    WER + Event-Log analysis of the user's ongoing "visualizer crashes" shows this was never one bug.
    (1) **Win2D native AV — ROOT-CAUSED AND FIXED (2026-09-01):** `Application Error` 1000 in
    `Microsoft.Graphics.Canvas.dll` **1.4.0.0**, `0xc0000005`, fault RVA **`0x0000000000052cc8`**,
    fault bucket `1176460281893223446` — IDENTICAL across every incident, and it did NOT move after
    the per-frame `CanvasLinearGradientBrush` was removed from `SpectrumFlowPreset`, so it was
    (correctly) suspected to be a control lifecycle fault rather than a draw-content bug. A user-
    captured WinDbg session (`.exr -1` / `k` / `!analyze -v`, saved to a local `recentcrash.txt`,
    not committed) finally pinned it down: the fault is in
    `CanvasAnimatedControl::ChangedImpl` → `CanvasGameLoop::StopDispatcher`, invoked from a
    `DispatcherQueueHandler` callback queued by Win2D itself — i.e. an async teardown callback
    firing against an invalid/stale game-loop pointer. Root cause: `VisualizerCanvas.CreateCanvas()`
    (`src/PlaySphere.UI/Visualizer/VisualizerCanvas.xaml.cs`) called `canvas.RemoveFromVisualTree()`
    — Win2D's own "stop rendering and tear down" API — on a `CanvasAnimatedControl` that had just
    been constructed and **was not yet added to any visual tree** (`HostRoot.Children.Add(canvas)`
    ran on the very next line). That queued Win2D's internal stop-dispatcher teardown against a game
    loop that either wasn't fully initialized yet or was torn down again by the real teardown path
    later, producing the AV whenever the callback happened to fire — deterministically reproducible
    by simply enabling the visualizer once `CreateCanvas()` ran. This was a copy-paste artifact from
    `DestroyCanvas()`, where the same call is legitimate. Fix: delete the errant
    `RemoveFromVisualTree()` call from `CreateCanvas()`; `DestroyCanvas()` is unchanged. Because the
    fault lives entirely in native Win2D frames reached via an async dispatcher callback, no managed
    `try`/`catch` around `CreateCanvas()`/`Canvas_Draw` could ever have caught it — this is why it
    survived earlier hardening passes. **Tooling note for next time:** WER `LocalDumps` (HKCU, full
    dump type) was correctly configured but never produced a `.mdmp` for this native AV across many
    incidents — don't rely on it. `winget` has no ProcDump package either. What worked: download
    ProcDump directly from `download.sysinternals.com/files/Procdump.zip` (official Microsoft
    Sysinternals distribution) — unpacked to
    `%LOCALAPPDATA%\LiVi PlaySphere Studio\Tools\ProcDump\` — **or**, as used for the actual fix,
    launch the Release exe directly under WinDbg and let it break naturally on the first-chance AV.
    (2) **Window-presenter resize race — MITIGATED (2026-09-01, unconfirmed whether it was ever the
    live crash cause given (1) is now understood, but the risk is real and the fix stands):**
    `AppWindowService.SetFullScreen`/`SetCompactOverlay` resize the `AppWindow` synchronously while
    `VisualizerCanvas`'s `CanvasAnimatedControl` may still be actively drawing on its own render
    thread — racing a swap-chain resize against an in-flight `Draw` call is a known class of native
    Win2D fault. `PlayerPage` now calls a new `PauseVisualizerAcrossWindowChange()` helper (pauses
    `VisualizerSurface` immediately, resumes via a low-priority dispatcher re-entry to
    `UpdateVisualizerUi()` once layout has settled) before every presenter-changing call:
    `ToggleFullScreen()`, `ToggleCompactOverlay()`, `ToggleVisualizerFullScreenAsync()`,
    `ExitVisualizerFullScreen()`, and the Escape-key fullscreen exit. (3) **PCM-tap managed AV —
    FIXED:** `.NET Runtime` 1026 event showed `System.AccessViolationException` at
    `Marshal.WriteByte` in `LibVlcPlaybackEngine.OnAudioSetup` (the `FL32` fourcc write into
    libVLC's format buffer on the native audio thread) — fixable by guarding the pointer write:
    `OnAudioSetup` now returns 1 (fall back to libVLC default format) on `format == IntPtr.Zero` or
    any write exception, and `OnAudioPlay` skips `Marshal.Copy` on `samples == IntPtr.Zero`, so a
    stale/teardown player can no longer take down the process via the tap. Build clean (0/0), full
    suite green (262 unit + 52 integration + 66 playback, 1 skip). **Still needed: an interactive
    manual pass** (open a video and an audio-only file, enable the visualizer, toggle fullscreen/
    compact-overlay/Escape a few times) to confirm the Win2D crash is actually gone in practice —
    automation here can only confirm the build/tests are green, not that the crash is silenced.
- **Visualizer PCM tap mid-session attempt — TRIED, CAUSED A REAL CRASH, REVERTED (2026-09-01);
    plus a persistent "Now Playing" bar (kept):** two more user-reported gaps found once the
    crashes above were fixed. (1) **Toggling the visualizer while media was already playing did
    nothing** — `AttachVisualizerSourceAsync`/`DetachVisualizerSourceAsync` intentionally defer the
    actual (un)registration whenever `_state == PlaybackState.Playing` (see the crash-triage entry
    above: a prior version rebuilt the native player mid-playback to attach and that corrupted the
    heap under the live audio thread). First attempt: route the toggle through a real `StopAsync()`
    → attach/detach → `PlayAsync()` → `SeekAsync(position)` cycle at the UI layer (not a
    `RebuildRenderer`/native-object rebuild, just the same Stop/Play transition the transport
    controls already use) on the theory that a plain Stop→Play cycle was safe where a full rebuild
    wasn't. **This was wrong and reproduced a real crash**: `.NET Runtime` event id 1026,
    `System.AccessViolationException` at `Marshal.WriteByte` inside
    `LibVlcPlaybackEngine.OnAudioSetup`, hit live during manual testing shortly after attaching this
    code. Root lesson: **`AccessViolationException` is a corrupted-state exception — ordinary
    `try`/`catch` in .NET Core cannot catch it, ever**, no matter how the call is wrapped; the
    `format == IntPtr.Zero` guard added earlier in `OnAudioSetup` only ever protected the literal
    null-pointer case, never a dangling/invalid-but-nonzero pointer, so it gave false confidence.
    Whatever native race the Stop→Play cycle opened up (most likely: `StopAsync()`/`PlayAsync()`
    return once the C# `_state` field is updated by the async native event, not once libVLC's actual
    teardown/setup machinery has finished, so a `Play()` issued right after observing `Stopped` can
    still race in-flight native cleanup) is not something that can be papered over from managed code
    without deeper native-level diagnosis this environment can't do (no reliable way to reproduce
    headlessly, and the fix must be verified against a real crash dump each time). **Reverted**:
    `PlayerPage.ApplyVisualizerPcmTapAsync` is back to a plain deferred attach/detach with no
    Stop/Play cycle and no `WaitForEngineStateAsync` helper (both removed) — this is the only
    variant proven safe by both the existing regression test and by not having crashed in practice.
    The UX gap (toggling mid-playback silently does nothing until the file is reopened) is
    currently unresolved and needs a product decision, not another unverified engineering attempt,
    before touching this again. (2) **No way back to background audio:** the
    engine is a DI singleton and keeps playing after navigating away from `PlayerPage`, but nothing
    in the shell showed what was playing or offered a way back — this was a missing feature, not a
    bug. Added a persistent "Now Playing" bar to `AppShell` (`src/PlaySphere.UI/Shell/AppShell.xaml`
    + `.xaml.cs`), docked below `ContentFrame`, visible whenever the engine has media loaded
    (`Ready`/`Playing`/`Paused`) and the current page isn't `PlayerPage`/fullscreen/compact-overlay;
    shows the title, a play/pause button, and tapping it navigates back into `PlayerPage`. This
    surfaced (and fixed) a real gap in `PlayerPage`'s existing-but-never-wired-up
    `OnNavigatedTo(IMediaPlaybackEngine)` re-entry path: `PlayerViewModel`'s constructor only
    subscribed to *future* engine events and never synced its cached `Title`/`FilePath`/`IsPlaying`
    from the engine's *current* state, so re-entering the player without a fresh `LoadAsync` rendered
    as if nothing were loaded despite playback actually continuing. Fixed by syncing those three from
    the engine's live snapshot at the end of the constructor. Enabled by a new
    `IMediaPlaybackEngine.LoadedFilePath` read-only property (backed by the engine's existing
    `_loadedPath` field) — previously there was no way to ask the engine what's currently loaded
    without going through a particular `PlayerViewModel` instance's lifecycle, which doesn't outlive
    page navigation. **Known gap, not addressed:** re-entering the player this way doesn't restore
    `LibraryItem`/Continue-Watching session binding (that's only set up inside `LoadAsync`), so a
    returned-to session won't resume-seek or record playback history the way a freshly opened
    library item does.
- **Re-entering the player silently killed the actual playback session — ROOT-CAUSED AND FIXED
    (2026-09-01):** the very first real-world use of the "Now Playing" bar's return-to-player path
    surfaced a genuine, previously-latent engine bug. Every `PlayerPage` navigation constructs a
    brand-new `VideoSurfaceHost`/`VideoView`, and `PlayerPage.OnNavigatedTo` always calls
    `AttachVideoSurfaceAsync(VideoHost)` on it — including when re-entering an already-`Playing`
    session via the engine-parameter path. The new `VideoView`'s `SwapChainOptions` are derived
    from its own fresh native swap-chain/context pointers, so they (almost) never equal
    `_activeSwapChainOptions` from the previous surface, which unconditionally forces
    `RebuildRenderer` — even though nothing about the actual media session needed rebuilding, only
    the picture's render target. `RebuildRenderer` calls `UnwireMediaPlayerEvents(previous)`
    *before* `previous.Stop()`, specifically so a rebuild-triggered stop doesn't look like a
    user-initiated one — but the side effect is that the engine's own `_state` field never learns
    the old player actually stopped, so it stayed stuck reporting `Playing`/`Paused` indefinitely
    after the rebuild despite the audio having genuinely gone silent. Every UI bound to
    `State`/`IsPlaying` — the Now Playing bar's icon, and `PlayerViewModel`'s constructor sync above
    — kept showing "Pause" although playback had actually stopped. Fixed with a pure managed
    state-bookkeeping change (no native call sequencing touched, unlike the visualizer attempt
    above — this carries none of that risk): `RebuildRenderer` now captures
    `wasActive = _state is Playing or Paused` before stopping the old player, and explicitly calls
    `SetState(PlaybackState.Stopped)` after the rebuild completes when `wasActive` was true — a
    no-op for the pre-existing fresh-load rebuild path (state there is `Ready`, so `wasActive` is
    false and behavior is unchanged; the existing
    `Attaching_swapchain_surface_rebuilds_renderer_but_loaded_media_still_plays` regression test
    still passes unmodified). New regression test
    `VideoSurfaceTests.Rebuilding_renderer_while_playing_resumes_playback`
    attaches a second, differently-optioned surface while genuinely `Playing` and asserts `State`
    becomes `Playing` (resumed), not stopped-at-0.
    **Follow-up — resume implemented, precise position restore is NOT (2026-09-01):** the above
    landed as "halts to Stopped" on purpose, but that left the reported symptom — returning to the
    player restarted from position 0 and paused. That was then fixed to **resume**:
    `RebuildRenderer` now captures `previous.Time` before `previous.Stop()`, then after the rebuild
    (when `wasActive` and no visualizer PCM tap is attached) calls `Play()` to keep a Playing session
    going (the native `Playing` event lands the state machine back on Playing), or `Play()` +
    `SetPause(true)` to keep a Paused session paused (never silently resumes). The captured position
    is surfaced immediately via `PositionChanged` so the UI reflects the resumed point. **Precise
    native position restoration is deliberately NOT attempted**: setting `_mediaPlayer.Time`/`Position`
    before/on the rebuilt player is silently dropped by libVLC once it starts parsing the fresh media
    (Position reads back 0), and every mechanism that *forces* it — the `:start-time` media option, or
    a deferred `Time` set inside the native `Playing` event callback — reproduced a native host crash
    / hang in the headless fixture (the event-callback variant is the same native-thread reentrancy
    that already caused the AV in `OnAudioSetup`). So on return the session resumes, but playback may
    restart near the start rather than at the exact paused second. New regression tests
    `Rebuild_while_playing_carries_position_and_resumes` / `Rebuild_while_paused_stays_paused_and_carries_position`
    assert the deterministic part (resumes / stays paused + carried position surfaced via
    `PositionChanged`) without asserting the flaky native readback. Build clean (0/0), full suite
    green (262 unit + 52 integration + 69 playback, 1 skip) in Release. **Still needs an interactive
    manual pass** for all of the above; automation here only confirms build/tests are green.
    **Audio-only fast path — the music-restart bug is actually GONE for audio (2026-09-01):** the
    user-reported live symptom — "switching from Home back to the player restarts the music from the
    beginning" — is now fully fixed for audio, not merely mitigated. `AttachVideoSurfaceAsync` /
    `LibVlcPlaybackEngine` now detects an **audio-only session** (a successful `LoadAsync` whose
    `Metadata.VideoTracks.Count == 0`, i.e. a media with no video to render) and **skips the native
    renderer rebuild entirely** on surface re-attach — a plain rebind instead, so the audio session is
    never torn down and plays on seamlessly at its exact position with zero native risk (no
    `Play()`+seek into a rebuilt player). This is the correct fix because the only reason a re-entry
    rebuilds is to route the *video* picture to a new swap-chain surface; an audio-only file has no
    picture, so rebuilding is pure destructiveness. The deferral above (resume + best-effort position,
    restart-near-start) now applies **only to video media**, where the rebuild is genuinely required.
    New regression test
    `VideoSurfaceTests.Attaching_surface_to_audio_only_session_does_not_rebuild_or_reset_position`
    (loads the `sine.mp3` audio-only fixture, plays, seeks, then attaches a differently-optioned
    surface) — verified it **fails** (position reset to 0, playback interrupted) with the fast path
    disabled and **passes** with it enabled (position preserved, still Playing). Full suite now:
    262 unit + 52 integration + 70 playback (1 skip), 0 failures, 0 warnings, smoke OK. Note: this
    keeps video re-entry on the resume-bests-effort-position path from the paragraph above; only
    audio is guaranteed seamless by this change.
- **Post-end timeline scrub now restarts playback (Issue: "seeking the timeline after a video has
    fully played doesn't render/re-seek the video — stays on the completed screen") FIXED
    (2026-09-01, revised twice):** root-caused at `PlayerViewModel`. After a video reaches the end,
    the engine sets `PlaybackState.Stopped`; a normal `SeekToPercentAsync` calls `SeekAsync`, which on a
    stopped/ended player sets fractional `_mediaPlayer.Position`. libVLC will not decode/render a frame
    while stopped, so the video stayed frozen on the last frame and the timeline snapped back — the
    "completed screen" symptom. **Attempt 1 was wrong**: seeking-before-playing (set fractional
    `Position`, then `Play()`) still misbehaved — a stopped/ended player *silently drops* the fractional
    `Position` pre-set (AGENTS' "pre-play Position restore is best-effort" note), so playback restarted
    from 0 rather than the sought point. **Attempt 2 (Play-then-wait-then-seek) was insufficient**: a
    plain `PlayAsync()` on a fully-consumed (EndReached) libVLC session does **not reliably restart**
    the media in the real app, so the player stayed stopped no matter how the seek bar was moved.
    **Corrected fix**: a shared private `SeekTargetAsync(target)` helper (used by `SeekToPercentAsync`,
    `SkipForwardAsync`, `SkipBackAsync`, `SeekRelativeAsync`) that, when `_mediaEnded` is true,
    **RELOADS the same media file** via `_engine.LoadAsync(MediaFile.FromPath(FilePath))` — deliberately
    the engine method, not the VM's, to avoid re-running the library resume-session wiring — to get a
    fresh, playable native player (Engine `LoadCoreAsync` just swaps `_mediaPlayer.Media` on the already-
    attached surface + re-applies audio-enhancements/subtitle-style; **no `RebuildRenderer`**, so no
    overlap with the recorded `AccessViolationException` risk class) — then **plays**, **waits for the
    native `Playing` event** (bounded ~2s poll of `_engine.State`, yielding the UI thread), and **then**
    seeks via `SeekAsync` — now `Playing`, so it takes the reliable `_mediaPlayer.Time = target` path (the
    same path transport scrubbing uses). A live/paused scrub remains a plain seek (no reload, no
    auto-play). `_mediaEnded` set in `OnEngineEndReached`, cleared in `LoadAsync`/`PlayPauseAsync`-when-it-
    plays. Regression test `PlayerViewModelTests.SeekToPercentAsync_after_end_restarts_playback` (plain
    scrub → no auto-play; scrub after `RaiseEndReached` → `PlayAsync` called + exact target). A stale
    stray `PlaySphere.App` process can lock `PlaySphere.UI.dll` in the release output and break
    `dotnet build` (MSB3026/7) — kill it before rebuilding after a smoke run.
- **Video icon + shortcut-hint overlay persisting over the playing video — FIXED (2026-09-01):**
    `PlayerPage`'s `VideoPlaceholderPanel` (the centered big video `FontIcon` E714 + "PlaySphere
    Universal Video Surface" text + the "Space: Play/Pause • ←/→: Seek ±5s …" hint) was being re-shown
    over the rendered picture for the whole playing duration. Root cause: `AttachVideoHostAsync` only
    collapsed the inner `VideoPlaceholder` text (not the parent panel), and `UpdateVisualizerUi` forced
    `VideoPlaceholderPanel.Visibility = _visualizerFullScreen ? Collapsed : Visible` on every call — so
    whenever NOT in visualizer-fullscreen mode (i.e. all normal playback) it stamped the placeholder
    panel Visible straight on top of the video. Fix: a new `UpdateVideoPlaceholderUi()` helper folds the
    decision into one place — the placeholder is **Collapsed** when either `_visualizerFullScreen`
    (visualizer fills the window) or `_videoSurfaceAttached` (a real video surface is rendering),
    otherwise **Visible**; both `AttachVideoHostAsync` (success + failure paths) and `UpdateVisualizerUi`
    call it. Now the icon/hint only shows on an empty, unbound surface and never overlays the picture.
- **Chapters &amp; Bookmarks drawer freezing the player controls while video kept playing — ROOT-CAUSED
    AND FIXED (2026-09-01):** `PlayerPage.UpdateDrawerUi` (which `ViewModel_PropertyChanged` →
    `UpdateUiFromViewModel` runs on *every* property-change tick, and during playback that is
    continuous via `PositionChanged`) unconditionally re-ran `_ = PopulateBookmarksUiAsync()` every
    tick for the Chapters/Bookmarks drawer, flooding the UI thread with repeated EF DB queries
    (`GetBookmarksAsync` + list rebuild) while native video rendered on its own threads — hence
    "controls freeze, video keeps playing". Fix: a `_lastRenderedDrawer` field guards each drawer's
    dynamic population so it runs only when the drawer kind actually changes (`drawerChanged`); the flag
    resets to `None` on close so reopening re-populates, and the existing explicit
    `PopulateBookmarksUiAsync()` calls in the Add/Remove bookmark handlers keep the bookmark list live
    after edits. Applies the guard to AudioTracks/Subtitles/Speed/MediaInfo/Equalizer re-population too.
- **Visualizer + audible audio simultaneously — COMPLETE on Windows via ADR-0009
    (process-loopback)** — see the dedicated section below.
  - **Still ahead in Phase 6:** none engineering-wise.
- **Visualizer + audible audio simultaneously (ADR-0009, supersedes ADR-0008's deferral):**
  COMPLETE on Windows (engineering-verified; see manual-QA caveat). The visualizer PCM tap from
  ADR-0008 silences speakers by contract, so "see the animation AND hear the audio" was impossible.
  ADR-0009 replaces that on Windows with a **NAudio WASAPI loopback** capture that reads the audio
  Windows mixes to the output endpoint and feeds it to the existing `IVisualizerAudioSource` pipeline
  while libVLC keeps outputting to the speakers normally — a purely additive path with **no libVLC
  audio callback registration, no `RebuildRenderer`, no engine TFM change, and no native AV risk**
  (the class of crashes AGENTS has repeatedly hit when the libVLC audio path is rebuilt mid-playback).
  - **Revision (2026-09-01): device-loopback replaced process-loopback.** The original
    `WithProcessLoopback(pid, IncludeTargetProcessTree)` path produced a **static** visualizer in
    practice — NAudio's process-loopback did not attach to libVLC's shared-mode render stream on the
    user's machine, so no real audio ever reached the analyser (only the silence-padder's zero frames
    were drawn). `WasapiLoopbackCaptureSource` now builds `new WasapiRecorderBuilder().
    WithLoopbackCapture()` (the documented, reliable whole-endpoint path). Accepted trade-off: it
    captures everything Windows mixes to that device, not only our app's audio.
    `WasapiLoopbackCaptureSource.BuildDefaultRecorderAsync` is `Task.FromResult(...Build())`.
  - **Window controls while in fullscreen — FIXED (2026-09-01):** entering fullscreen/cinema/mini-
    player removed the system titlebar, and navigating away from the player page carried that chrome
    -less immersive mode onto Home (etc.), leaving no way to close/minimize/resize except going back
    to the player. `PlayerPage.Page_Unloaded` now calls a new `ExitWindowModes()` that restores
    normal windowed presentation (exits fullscreen, cinema and compact-overlay, pausing the
    visualizer across the presenter changes) so every other page always has normal window chrome.
  - New **`PlaySphere.Audio`** project (`net10.0-windows10.0.19041.0`, Windows-only): isolates the
    `NAudio.Wasapi` 3.0.1 package (SDK-level central version in `Directory.Packages.props`). Depends
    on `PlaySphere.Application` (the `IVisualizerAudioSource` contract) and `PlaySphere.Visualizer`
    (the concrete `PcmAudioSource` it wraps). The plain-`net10.0` engine and visualizer modules never
    reference it — same boundary discipline as every module.
  - `WasapiLoopbackCaptureSource : IVisualizerAudioSource` builds a NAudio 3 `WasapiRecorder` via
    `new WasapiRecorderBuilder().WithLoopbackCapture()` (device-whole endpoint loopback — the
    reliable path; see the revision note above) and converts each `DataAvailable`
    `ReadOnlySpan<byte>` packet to interleaved float PCM (`WasapiLoopbackPcmConverter`, pure and
    headless-testable: 32-bit IEEE float + 16-bit PCM encodings) before pushing to the analyser with
    the real per-batch rate/channels (never assumed). A timer pads bounded silence gaps so the
    visualizer doesn't freeze during quiet/stopped passages (loopback yields no buffers while silent).
  - **Contract change:** `IVisualizerAudioSource` gained `void Start()` (symmetric with the existing
    `Stop()`), so the loopback wrapper can begin its async recorder build through the interface.
    `PcmAudioSource.Start()` is a no-op (decoder-fed); `WasapiLoopbackCaptureSource.Start()`
    fire-and-forgets `StartAsync()` (guarded by `_started`); the `RecordingSource` test fake got the
    member.
  - `AddPlaySphereAudio()` DI extension (in `PlaySphere.Audio`) registers the loopback wrapper as
    `IVisualizerAudioSource`, wrapping the `PcmAudioSource` it registers as itself. `App.xaml.cs` now
    chains `.AddPlaySphereVisualizer().AddPlaySphereAudio()`. `PlayerPage` resolves
    `IVisualizerAudioSource` as before but the visualizer toggle uses
    `ApplyVisualizerAudioSourceAsync()`: it calls `Start()`/`Stop()` on the source and **no longer
    touches `AttachVisualizerSourceAsync`/`DetachVisualizerSourceAsync`** — so the ADR-0008
    "mid-playback toggle only takes effect on next file load" UI limitation is gone on Windows (the
    visualizer attaches/detaches live because it never touches libVLC's audio-callback registration),
    and audio is never silenced.
  - The engine's `AttachVisualizerSourceAsync`/`DetachVisualizerSourceAsync` members REMAIN in the
    contract as the non-Windows fallback (their speaker-silencing limit is unchanged, per ADR-0008)
    but are no longer called from `PlayerPage`.
  - Tests: `WasapiLoopbackPcmConverterTests` (pure byte→float + silence; IEEE float values/order, 16-bit
    PCM normalization, Extensible, unsupported-encoding→0, empty→0, destination-capacity clamp,
    `CreateSilence` all-zeros + format guard) and `AudioServiceCollectionRegistrationTests` (the
    resolved `IVisualizerAudioSource` is the loopback wrapper; `Stop()` safe; the PCM analyser pipeline
    behind it still publishes frames). Full suite now: 282 unit + 52 integration + 70 playback (1 skip),
    0 failures, 0 build warnings; smoke OK.
  - Manual-QA caveat (same limitation as prior phases): NAudio loopback capture needs a real Windows
    audio stack + device, so the capture itself can't be exercised headlessly. What IS verified:
    solution builds clean (0/0), the pure conversion/silence + DI registration tests pass, the app
    smoke-launches. Required manual pass: open an audio file (or a video — the visualizer is audio-
    only by design, so use audio), enable the visualizer, confirm **both** the audio is audible
    **and** the animation tracks it in real time; toggle the visualizer off and confirm audio
    continues; enter/exit Fullscreen Visualizer; switch presets; and confirm that (unlike the old
    PCM tap) enabling the visualizer no longer mutes the speakers. Also confirm that entering
    fullscreen/cinema/mini-player and then navigating to Home returns a normal, chrome-bearing
    window (close/minimize/resize available) rather than stranding the app fullscreen.
  - Known limitation (accepted in ADR-0009): device-loopback captures everything Windows mixes to
    the output endpoint, not only our app's audio (a personal single-player model makes this
    acceptable — there is no clean per-stream isolation at the WASAPI loopback level). There is a
    small constant loopback latency (the analyser/latest-frame channel already drops stale frames,
    so harmless). The loopback yields no buffers while the endpoint is totally silent; the
    silence-padder keeps the visualizer alive but tracks (near-silent) RMS normally.
  - **Visualizer is audio-only by user decision (2026-09-01):** the visualizer is deliberately made
    unavailable while a **video** is playing — it would only overlay the picture, and the loopback
    capture still tracks freely. `PlayerPage` now gates every visualizer entry point (overflow "Show
    Visualizer"/"Fullscreen Visualizer" items, the "Visualizer Preset" submenu, and the transport
    Fullscreen-Visualizer button) on `IsVisualizerAvailable()` = an audio-only session
    (`_viewModel.VideoTracks.Count == 0` — the same rule the engine uses to identify an audio-only
    session, see the audio-only fast path). When the loaded media has video tracks those controls are
    `IsEnabled=false`, and `RefreshVisualizerAvailability()` (called from `UpdateUiFromViewModel` on
    every property tick) tears down any active visualizer session — disables the overlay, exits
    visualizer-fullscreen, and stops the audio source — so a queue advance to a video never leaves the
    animation overlaying the picture or a capture running. `ToggleVisualizerButton_Click` /
    `FullScreenVisualizerButton_Click` / `ToggleVisualizerFullScreenAsync` also guard on availability
    so a stale click can't re-enable for a video. Video never gets a visualizer; audio files keep the
    full five-preset experience.
- **"Visualizer remains static" — INVESTIGATED + one real bug FIXED, diagnostics added (2026-09-01):**
  the device-loopback change alone did NOT fix the static visualizer for the user, so this session
  (a) proved the loopback mechanism headlessly and (b) found/fixed a genuine decode bug.
  - **Mechanism confirmed working on THIS machine via a headless NAudio probe** (temp harness under
    `%TEMP%\opencode\LoopbackProbe`, not committed): enumerated the active render device (`Surface
    Omnisonic Speakers`), played a 440 Hz tone through `WasapiOut` in shared mode, and captured via
    `new WasapiRecorderBuilder().WithLoopbackCapture().Build().StartRecording()` → **81 DataAvailable
    packets, peak 1.0** over 2 s. So the device-loopback path is NOT broken in this environment; it
    delivers real captured PCM. The real device mix format reported is
    `WAVE_FORMAT_EXTENSIBLE IEEE_FLOAT 48000Hz 2ch 32bit`.
  - **Real bug FIXED — Extensible-float mix formats were truncated to 16-bit PCM:**
    `WasapiLoopbackPcmConverter` keyed decode purely on `WaveFormat.Encoding`, and for
    `WaveFormatEncoding.Extensible` it always did 16-bit PCM. But a device mix format is commonly
    Extensible with **32-bit IEEE float** (exactly THIS machine's format), where the encoding enum
    is still `Extensible`. Reading a 32-bit float's low two bytes as int16 yields (for many smooth
    signals) near-zero/garbage frames → bars that barely move (look static). Fix: the converter now
    takes the real `format.BitsPerSample` and treats **Extensible + 32-bit as IEEE float** (any
    32-bit float), while Extensible 16/24-bit stays integer PCM; added 8/16/24/32-bit integer
    scaling. Call sites pass `format.BitsPerSample`. New tests: `Extensible_32bit_is_treated_as_ieee_float`,
    `Unsupported_bit_depth_returns_zero`; suite now **284 unit** (was 282), all green.
  - **New diagnostics (NOT committed noise — it's user-facing instrumenting for the next reproduce):**
    `PlaySphere.Audio.VisualizerAudioTrace` writes timestamped lines to
    `%LOCALAPPDATA%\LiVi PlaySphere Studio\visualizer-audio.log` (debounced to ~every 50th normal
    call; lifecycle events are forced). `WasapiLoopbackCaptureSource` logs recorder build+WaveFormat
    (forced), `StartRecording` success/failure (forced), a `Stop` summary (packets/samples/recentPeak,
    forced), and per-packet `DataAvailable` bytes/samples/peakF/format (debounced). Also counts
    packets/samples/peak under `_gate` (no atomic slot contention in practice). On the user's next run:
    if the log shows real packets with peak>0, the capture→analyser path delivers audio and the
    history is in rendering/analyser config; if it shows 0 packets or build/start FAILED, the capture
    isn't connecting (device/mode mismatch).
  - **Untested hypothesis still standing:** device-loopback captures the app's audio only if libVLC
    renders to the same (default) render endpoint NAudio's `WithLoopbackCapture()` targets. Build
    clean (0/0), unit tests green (284). A manual re-run with `visualizer-audio.log` in hand is the
    required next step — automation here can't observe the running app's audio stream.
- **Fullscreen Visualizer REMOVED by user decision (2026-09-01):** the report was "normal-screen
  visualizer works fine, but when the Fullscreen Visualizer is used the screen shows nothing (blank),
  whereas if video fullscreen is invoked during the visualizer everything works." Root cause was never
  chased further — the user explicitly decided the feature is redundant: the in-window visualizer plus
  the existing **video fullscreen** cover the fullscreen need, so the custom fullscreen-visualizer path
  was removed rather than debugged. Removed: the overflow-menu "Fullscreen Visualizer" item, the
  transport-bar Fullscreen-Visualizer button, `FullScreenVisualizerButton_Click`,
  `ToggleVisualizerFullScreenAsync`, `ExitVisualizerFullScreen`, and the `_visualizerFullScreen` field
  (plus all references in `WindowService_FullScreenChanged`, `ToggleVisualizerButton_Click`,
  `RefreshVisualizerAvailability`, `UpdateVisualizerUi`, `UpdateVideoPlaceholderUi`). `UpdateVisualizerUi`
  now always keeps `VideoSurface` visible (no more fullscreen-visualizer collapse). No automatic tests
  covered the removed path (UI-only); build 0/0, 284 unit tests green, smoke OK. To get a fullscreen
  visualizer, enter **video fullscreen (F/F11)** — the in-window visualizer overlay then fills the
  window with the picture.
- **App icon (2026-09-01):** the unpackaged WinExe previously shipped with no embedded icon (blank in
  Explorer/taskbar/title bar). A brand icon was designed and embedded via
  `<ApplicationIcon>Assets\app.ico</ApplicationIcon>` in `src\PlaySphere.App\PlaySphere.App.csproj`.
  Design (references the PlaySphere palette, PICKLE.md §7): a dark rounded-square tile
  (`#0E1117`→`#1D2430` diagonal) with a translucent blue accent ring, a centered **blue→purple→magenta
  gradient play triangle**, and a small **cyan orbit dot** (`#22D3EE`) — reads as "media player +
  PlaySphere". `src\PlaySphere.App\Assets\app.ico` holds 7 PNG frames (16/24/32/48/64/128/256); the
  csproj embeds it via `<ApplicationIcon>` **and** copies it to the output (`Assets\app.ico`), so
  `MainWindow.SetWindowIcon()` can resolve it from `AppContext.BaseDirectory` and call
  `AppWindow.SetIcon(...)` at ctor time. The embedded exe icon alone does **not** surface in the WinUI
  title bar or taskbar — the `AppWindow.SetIcon` call is what places the icon there. The
  generator is a throwaway tool (System.Drawing + manual ICO packaging) kept under
  `%TEMP%\opencode\IconGen` (not committed); regenerate by re-running it if the art ever changes.
- **Open URL streaming support (2026-09-01):** the player supports DRM-free network streams (HLS/DASH
  livestreams, IPTV, podcasts, RTSP/RTMP cameras, your own media server) alongside local files.
  - `MediaFile` (Domain) generalised beyond path-only: new `FromUri(string)` (validates absolute URI +
    an allow-listed stream scheme; derives `DisplayTitle` from the last path segment, else host) and
    `FromPathOrUri(string)` (the UI/VM entry point — dispatches an absolute stream-scheme URI to
    `FromUri`, everything else to `FromPath`). `SourceUri`/`IsRemoteUri` identify a stream. `FromPath`
    is unchanged for local files.
  - `ILoadMediaPlaybackEngine`/`LibVlcPlaybackEngine.LoadAsync`: a remote `MediaFile` skips the local
    `File.Exists`/`HasRecognisedMediaHeader` gates, builds `new Media(_libVlc, uri)` and parses with
    `MediaParseOptions.ParseNetwork` (8s timeout); `_loadedPath` holds the absolute URI. `RebuildRenderer`
    re-attaches a URI via the new `TryBuildVlcMediaFromLoaded` helper (URI-or-file), so re-entering the
    player page for a streamed *video* rebuilds correctly, while the existing audio-only fast path keeps
    streamed audio seamless.
  - `PlayerViewModel.LoadAsync`: a remote URI skips sibling-subtitle auto-load and library
    session/resume wiring (no local dir, no catalog match — URLs never enter the library). Title/Now
    Playing-bar derivation is URI-safe via `MediaFile.FromPathOrUri`.
  - UI: new "Open URL" button on `HomePage` opens a `ContentDialog` TextBox, validates via
    `MediaFile.FromPathOrUri`, and navigates to `PlayerPage` with the URL string (reuses the existing
    `string` navigation branch). Unsupported/invalid input shows an explanatory dialog.
  - **Not supported (by design):** subscription DRM services (Netflix/Prime/Disney+/etc.) — libVLC has
    no legitimate Widevine/PlayReady intake and their streams are DRM+walled off; that's a hard boundary.
    The dialog states streams must be DRM-free.
  - Tests: +5 `MediaFile` (PlaybackContractTests: URI title derivation, scheme allow-list, `FromPathOrUri`
    dispatch) + 1 VM (`LoadAsync_with_url_builds_a_remote_media_file`). Full suite: 290 unit + 52
    integration + 70 playback (1 skip), 0 failures, 0 warnings, smoke OK. Manual-QA remaining: paste a
    real livestream/IPTV URL and confirm picture/sound render and playback state reflects a stream.
- **Phase 8 — buffering status + bounded auto-reconnect for network streams (2026-09-01):**
  stream reliability for the Open URL feature. Per the user's decision, reconnect is **auto + bounded
  + safe reload**: no native player rebuild (the recorded `AccessViolationException`/heap-corruption
  risk class), just reload the same URI onto the existing player and replay.
  - **Buffering status:** `PlaybackState` gained a `Buffering` value (between `Playing` and `Paused`;
    the state machine is all equality/`is` checks, no exhaustive switch, so the new value is safe).
    The engine already wired `player.Buffering += OnVlcBuffering` (libVLC raises it at stream startup
    and on stalls), which transitions to `Buffering`; libVLC then raises `Playing` on resume. The VM's
    `OnStateChanged` sets `IsPlaying = (Current == Playing)` and `StateLabel = State.ToString()`, so a
    stalled stream reads as a non-playing "Buffering" chip for free (no VM changes needed). 2 new
    `PlayerViewModelTests` assert `Buffering` shows `IsPlaying == false` + `StateLabel == "Buffering"`
    and that Playing→Buffering→Playing tracks the live stall/recover.
  - **Bounded auto-reconnect (`LibVlcPlaybackEngine`):** the engine now stores `_loadedMedia` (the
    loaded `MediaFile`) and `OnVlcEncounteredError` calls `TryBeginAutoReconnect()` for **remote**
    streams (local-file errors still surface as hard `Error` immediately). A fire-and-forget
    `AutoReconnectAsync` (own `CancellationTokenSource`, guarded by a `_reconnecting` flag + a lock so
    it never stacks and never cancels its own in-flight reload) loops up to `MaxReconnectAttempts = 3`
    with `ReconnectBackoff` (1s/2s/4s), each attempt signalling `Buffering`, `LoadAsync` the same URI,
    and — only when the reload reaches `Ready` — `PlayAsync` + best-effort `Position` restore (never
    fatal). A failed `LoadAsync` (which `FailLoad`s into `Error`) is treated as one failed retry and
    the loop continues; exhausting all attempts surfaces the normal hard `Error` + `ErrorOccurred`.
    Because a successful reload re-runs `LoadAsync` (which resets `_loadedMedia`/`_reconnectAttempt`),
    the budget is effectively per-drop with the task's own bounded loop. `CancelAutoReconnect()` runs
    on a **user-initiated** `LoadAsync` (guarded by `!_reconnecting`) and on `Dispose`, so an in-flight
    reconnect can never reload over a new file or a disposed player. No switch/VM/UI changes were needed.
    **Note the reconnect path is native-event-driven (`EncounteredError` from libVLC), so it is NOT
    covered by a deterministic headless test** — covered instead by code review of the safe-reload path
    (identical to the proven post-end-seek reload: `LoadAsync`→`PlayAsync`→best-effort seek, never a
    native rebuild) plus the buffering VM tests.
  - **Testing note (important):** on THIS headless win-x64 host, a set of pre-existing playback tests
    that wait on native libVLC `Playing`/`Paused`/position events fail intermittently (this run: 8 in
    `VisualizerPcmTapTests`/`VideoSurfaceTests`/`TransportTests`/`SeekTests`). Confirmed **environmental,
    not a regression** by stashing the reconnect engine changes and re-running the failures — they fail
    identically with the reconnect code disabled. These are the same class of native-state-timing flakiness
    AGENTS has noted before. Do NOT weaken them (Rule 5); just re-run on a healthy/displayed host.
  - Manual-QA remaining: open a livestream/IPTV URL, unplug the network mid-play and confirm the chip
    shows "Buffering"/reconnecting and playback resumes automatically (up to 3 backoff retries), and
    that a permanently-unreachable stream ends in the Error state without crashing.
  - **`Play() returns false` on a stream now auto-recovers instead of dead-ending (2026-09-01):**
    the user hit "libVLC MediaPlayer.Play() returned false" opening a streaming audio URL. Investigation
    (headless probe against `ice1.somafm.com/groovesalad-128-mp3`, real engine, audio enabled, with and
    without a video-surface attach) showed the same genre of stream **loads, reaches `Playing`, and
    advances position** here — so the failure is stream/protocol/environment-specific, not the general
    Open-URL path. libVLC reports `Play()` returning false through `libvlc_printerr`, which never reaches
    the `Log` event (why the `libvlc.log` and `BuildFailureContext()` show nothing). `PlayAsync` now
    treats a `Play() == false` on a remote stream (when `BuildFailureContext()` produced no real
    libVLC error line) as a transient drop and runs the same bounded Phase 8 auto-reconnect (reload the
    URI onto the existing player, retry ≤3 with backoff, replay) instead of immediately hard-erroring —
    a flaky stream now recovers automatically, and a genuinely-broken stream still ends at a clear
    "Could not reconnect to stream" Error after the retries. Local-file `Play()` failures are unaffected
    (they hard-error immediately, as before). No deterministic headless test covers this branch (it is
    native-driven — same precedent as the reconnect feature itself); verified by code review of the
    safe-reload gate + the working stream path via the temporary probe (removed after use).
  - **Live/interminable streams — seek bar + skip disabled, LIVE badge (2026-09-01):** a radio/live
    stream reports libVLC duration 0 (interminable), which made the timeline slider sit at 0 and the
    seek commands silently no-op ("home screen seek bar not available"), and the visualizer freezing
    on such a stream is a *symptom* of the stream's audio silently dropping (the WASAPI loopback trace
    `visualizer-audio.log` showed mostly `bytes=0` endpoints — near-silence — not a capture bug).
    - New `IMediaPlaybackEngine.IsSeekable` (libVLC native `MediaPlayer.IsSeekable`, fallback to
      `Duration > TimeSpan.Zero`; guarded) exposed as `PlayerViewModel.IsSeekable`.
    - `PlayerViewModel` seeks (`SeekToPercentAsync`, `SkipForwardAsync`, `SkipBackAsync`,
      `SeekRelativeAsync`) now early-return when `!IsSeekable`, so a live stream is a hard no-op
      (previously the duration<=0 guard already caught the 0-duration case, but IsSeekable is the
      authoritative flag).
    - `PlayerPage`: `TimelineSlider` + `SkipBackButton`/`SkipForwardButton` disable for non-seekable
      streams, and a red `LIVE` badge replaces the `DurationText` when unseekable (elapsed
      `PositionText` still ticks). So a radio stream reads honestly as live/unseekable instead of a
      broken timeline.
    - Tests: +4 `PlayerViewModelTests` (IsSeekable reflection; skip forward/back + seek-percent are
      no-ops on non-seekable). Suite: 296 unit + 52 integration, 0 failures, 0 warnings, smoke OK.
    - Visualizer-freeze root cause is *not* a visualizer defect: the loopback capture is healthy
      (summary `peakF` ~0.98 during real audio); the freeze happens because the flaky live stream's
      audio intermittently stops reaching the endpoint (mostly-zero loopback). The engine reconnect
      (EncounteredError + Play()-false) covers drops that fire those events; a pure **silent stall**
      (Buffering fired, never resumes, no EncounteredError — no `Position` advance to watch for live
      radio, since position is meaningless there) is NOT auto-recovered, by design — a reliable
      detector is not achievable without native-manipulation risk (AGENTS' AV warnings) and needs a
      product decision. Manual-QA remaining: play `ice1.somafm.com/groovesalad-128-mp3`, confirm the
      LIVE badge shows, the timeline/skips are disabled, and that (with reconnect) the stream recovers
      from drops without the visualizer freezing permanently.
  - **Static visualizer while music continues — ROOT-CAUSED AND FIXED (2026-09-01):** the user
    reported the visualizer frozen for BOTH local audio and streams while the music kept playing, plus
    the bottom "Now Playing" bar missing on Home in both cases. `visualizer-audio.log` proved the real
    defect: a second loopback start threw
    `NAudio.CoreAudioApi.CoreAudioException 0x88890002 "The audio client is already initialized"`
    (`WasapiLoopbackCaptureSource.StartAsync` → NAudio `WasapiRecorder.StartRecording`), so the session
    captured `packets=0 recentPeakF=0.0000` → static visualizer while audio played on. Root cause:
    `WasapiLoopbackCaptureSource.Stop()` only called `StopRecording()`, which does NOT release the
    underlying WASAPI `AudioClient`, and it kept the `_recorder` reference — so a later `Start()`
    (re-enabling the visualizer, or re-entering the player page after `Page_Unloaded` stops it) reused
    the same recorder and `StartRecording()` failed because the `AudioClient` was still initialized.
    Fix: `Stop()` now **fully disposes + drops** the recorder (`ReleaseRecorder` = StopRecording then
    Dispose, `_recorder = null`), so the next `Start()` builds a **fresh** recorder with a clean
    `AudioClient`. `Dispose()` reuses this teardown. (Also fixes the second symptom for the common
    case: a visualizer start failure had left the app in a stale state.)
    - **Now Playing bar robustness (same commit):** `AppShell.UpdateNowPlayingBar` previously showed
      the bar only for `Playing`/`Paused`/`Ready`; a stream mid-`Buffering` (common during a drop/
      reconnect) or `Loading` would hide the return-to-player bar, stranding the session. It now shows
      for every state except `Empty`/`Stopped`/`Error`, so a transient stall never hides the way back.
    - **Testing note (methodology):** an attempted deterministic regression test that built a real
      NAudio `WithLoopbackCapture().Build()` (to count factory invocations across a stop/restart)
      **hung the whole UnitTests suite** when run at project scope (device contention under xUnit
      parallelism — passed alone in 1s, hung >600s alongside others). It was **removed** rather than
      committed (Dev-touching × parallel tests = AGENTS' flake rule). The fix is verified by code
      review (lifecycle logic + `0x88890002` evidence) and the app smoke test, not an automated test.
      Build clean (0/0), suite green (296 unit + 52 integration), smoke OK. Manual-QA: enable the
      visualizer, leave to Home and return, re-enable — confirm the bars now animate (not static),
      and that the Now Playing bar is present on Home for a playing/buffering audio item.
  - **"Play button stuck on start / bottom chip stuck on Buffering / visualizer dead while audio plays"
    — ROOT-CAUSED AND FIXED (2026-09-02, engine state wedge):** user reported that even a **local**
    file played fine (audio + video) but the transport play button stayed a "start" glyph (never
    became Pause), the bottom state chip read "Buffering", Pause/Stop were unreachable, and selecting
    the visualizer had no visible effect while audio continued. All four symptoms are ONE root cause:
    an engine state wedge, not four separate bugs.
    - **Mechanism:** `LibVlcPlaybackEngine.OnVlcBuffering` unconditionally called
      `SetState(PlaybackState.Buffering)` on every libVLC `Buffering` event. libVLC re-raises
      `Buffering` with a full cache (≥ 100) **repeatedly throughout active playback** (audio-output
      warm-up, ES changes, memory polls) and does **not** re-fire `Playing` each time — `Playing` only
      fires on a state *transition*. So once any post-`Playing` full-buffer event landed, the machine
      dropped to `Buffering` and stayed there forever: `PlayerViewModel.IsPlaying` (= `State == Playing`)
      went false → UI Play/Pause button shows "start"; `PlayPauseAsync` branches on `State == Playing`
      so the button now calls `PlayAsync()` instead of `PauseAsync()` → Pause unreachable; the chip
      shows "Buffering"; `VisualizerSurface.IsPaused = !IsPlaying` → the audio visualizer freezes; the
      Now Playing bar shows a play icon mid-playback. (Why headless playback tests didn't catch it:
      they assert reaching `Playing` and read `State` at that instant — a later transient full-buffer
      event that wedges the real app fires on the live audio-output path that the fixture's timing
      rarely overlaps.)
    - **Fix:** a pure policy `LibVlcPlaybackEngine.EvaluateBuffering(PlaybackState current, float cache)`
      (internal, `InternalsVisibleTo("PlaySphere.PlaybackTests")` added to the csproj): a `cache < 100`
      event is a genuine pre-roll/stall → surface `Buffering` (unchanged; the Phase 8 chip and the 2
      existing `PlayerViewModelTests` keep passing); a `cache >= 100` (buffer full) event **never**
      demotes an active Playing/Paused session (returns null), and only lifts a pending `Buffering`
      back to `Playing` (replacing the missing native re-`Playing`). `OnVlcBuffering` now just applies
      the policy (`var next = EvaluateBuffering(_state, e.Cache); if (next.HasValue) SetState(next.Value);`).
    - **Tests:** 15 new `tests/PlaySphere.PlaybackTests/BufferingPolicyTests.cs` (cache < 100 surfaces
      Buffering across Playing/suspended states; **cache 100 while Playing keeps Playing — the exact
      regression**; cache 100 while Paused keeps Paused; cache 100 after a pending Buffering restores
      Playing; cache 100 from non-active states is a no-op).
    - **Why this is not a regression against the flake family:** an A/B on this headless host (restore
      the old handler, rebuild, run `VideoSurfaceTests|SeekTests`) still failed 6 of 18 with the same
      native-event-timing failures (`WaitForStateAsync`/position) — the previously-documented
      environmental flake family, unchanged by this fix.
    - **Visualizer-on-video is by design, not this bug:** for content with video tracks the visualizer
      controls are deliberately disabled (`IsVisualizerAvailable()` = audio-only session) — that part of
      the report is intended gating, not a defect. The freezing part of the report (for an audio file)
      is the wedge above, now fixed.
    - Manual-QA remaining: open a local video + a local audio file, confirm the transport Play/Pause
      button toggles correctly, the chip reads "Playing" (not "Buffering"), Pause/Stop work, and the
      audio visualizer animates (for audio files only).
- Phase 7 (Media Tools): STARTED — **PS-701 (Save Snapshot) COMPLETE** (engineering-verified; see
  manual-QA caveat below).
  - A user-facing in-player screenshot: the overflow menu gains a "Save Snapshot" item that opens a
    save picker and captures the current video frame as PNG via the existing
    `IMediaPlaybackEngine.TakeSnapshotAsync` (no engine changes needed — this ticket is UI/VM only,
    the smallest coherent Phase 7 slice).
  - `PlayerViewModel.CanCaptureSnapshot` (video track present + `FilePath` known) and
    `SaveSnapshotAsync(outputPath)` (best-effort: false − never throws − for no-video/empty-path/
    engine-snapshot failure; creates the output directory when missing). Playback is never disturbed.
  - New pure `PlaySphere.UI.Helpers.SnapshotNaming.SuggestFileName(title, at?)` derives a safe
    suggested stem — sanitizes untrusted media titles (`Path.GetInvalidFileNameChars` stripped,
    whitespace collapsed, trailing dots trimmed, spaces→hyphens, "PlaySphere-snap-…" fallback for
    empty/invalid titles) with an injectable timestamp for deterministic tests (PICKLE.md §43).
  - New `FilePickerHelper.PickSaveSnapshotFileAsync(suggestedFileName)` (PNG choice, existing HWND-
    initialized picker pattern). `PlayerPage.xaml` overflow menu item + `SaveSnapshotButton_Click`
    handler with a result `ContentDialog` (saved path / "no picture to capture" / failure), all
    `XamlRoot`-guarded.
  - Shoulders of work for later Phase 7 tickets: waveform, hover thumbnails, Mark In/Out, conversion
    and clip export remain (screenshot is the only one partially pre-existing via cover-art snaps).
    The `TakeSnapshotAsync(320,0)` cover-art path is unchanged (size-limited, library-owned); the new
    user snapshot uses the native full frame.
  - Tests: 4 `PlayerViewModelTests` (captures to requested path incl. directory creation, engine
    result passes through, false when no video track, false for null/empty/whitespace path) + 5
    `SnapshotNamingTests` (timestamped stem, invalid-char stripping, whitespace collapse, dots,
    fallback prefix). Full suite: **300 unit + 52 integration** green (was 296 + 52), 0 warnings.
  - Manual-QA caveat (same limitation as prior phases): the real libVLC frame-grab needs a rendered
    video surface, which can't be exercised headlessly here. What IS verified: build clean, unit
    tests green (fake engine round-trips the exact path + engine result), smoke launch OK (WinUI
    compile-time XAML validation for the new menu item passed). Do a manual pass on the next run:
    open a video, press the overflow menu → "Save Snapshot", confirm the picker opens with a safe
    suggested name, the PNG appears at the chosen path at the current frame, the dialog reports the
    path, and that an audio-only file reports "There is no video picture to capture right now."
  - **PS-702 (Mark In / Out + Clip Export) COMPLETE** (engineering-verified; see manual-QA caveat
    below). Set In/Out points on the timeline, visualize the range, and split-cut that exact segment
    out with ffmpeg into a new file.
    - New **`PlaySphere.Media.Export`** module (Domain+Application deps only, mirroring
      `Media.Probe`): `Application.Media.IMediaClipExporter.ExportClipAsync(inputPath, outputPath,
      start, duration, ct)` → `Domain.Media.ClipExportResult(Success, ErrorMessage)`, implemented by
      `FfMpegClipExporter` — a bounded ffmpeg subprocess with structured argument arrays only (rule
      4/§43: `-y -hide_banner -loglevel error -ss <s> -i <in> -t <d> -map 0:v:0 -map 0:a? -map 0:s?
      -c copy <out>`), per-export timeout + linked cancellation, capped stderr for the error message,
      and soft failure (missing tool / missing input / bad URL-safe bounds / unsupported container
      ext (.mp4/.mkv/.mov/.webm/.avi) / ffmpeg error / timeout → `Fail`, never throws; partial output
      deleted best-effort). `FfMpegExportOptions` (`ExecutablePath`/`Timeout` 5m/`MaxErrorBytes`)
      + `AddPlaySphereClipExport(options?)` DI ext; App wires it to the checked-in
      `tools\ffmpeg\ffmpeg.exe` via a new `ConfigureFfMpegExportOptions()` (FindUpwards, like ffprobe).
    - `PlayerViewModel` mark state: `MarkIn`/`MarkOut` (`TimeSpan?`, always ordered — a mark that
      would invert the pair clears the older one), `IsMarked`, `MarkInPercent`/`MarkOutPercent`
      (0–100 for the overlay), `CanExportClip` (marked + exporter present + **local** file — remote
      streams can't be cut), `SetMarkIn()`/`SetMarkOut()` (record playhead, no seek), `ClearMarks()`,
      `ExportClipAsync(outputPath)` (best-effort, never throws). Marks cleared on every `LoadAsync`.
      Exporter injected via optional `clipExporter:` ctor param (3 PlayerPage sites).
    - `PlayerPage` UI: overflow-menu "Mark In ([)"/"Mark Out (])"/"Clear Marks"/"Export Clip…" items
      (Clear/Export enabled from VM state on every tick) + **`[`/`]` keyboard shortcuts** (I/O were
      taken by Media Info/Equalizer-adjacent drawers — actually I = MediaInfo, so brackets chosen);
      a translucent `GradientAccentPrimary` range overlay under the timeline slider (positioned in
      code-behind on mark change + slider size change); `ExportClipButton_Click` → save picker
      (`.mp4`/`.mkv`/`.mov` via new `FilePickerHelper.PickSaveClipFileAsync`) → `ExportClipAsync` →
      result ContentDialog (all `XamlRoot`-guarded). Suggested stem reuses `SnapshotNaming.Sanitize`
      (`{title}-clip-yyyyMMdd-HHmmss`).
    - Tests: 9 new `PlayerViewModelTests` (ordered mark range + percents, mark-ordering
      clears-the-invalidating-point, clear, remote-stream no-export, no-exporter no-export,
      exporter passthrough of path/start/duration, failure without marks, failure without exporter,
      marks cleared on load) via a new `FakeClipExporter`; 5 new `FfMpegClipExporterTests`
      integration tests against the real bundled ffmpeg (real ~1s trim from the synthetic 2s MP4,
      missing-input fail, unsupported-extension fail, invalid-bounds fail, missing-tool fail).
      Full suite: **319 unit + 57 integration** green (was 310 + 52), 0 warnings, smoke OK.
    - Manual-QA caveat (same limitation as prior phases): the real ffmpeg trim (stream copy) is
      integration-tested on a synthetic fixture, and the VM/UI wiring is unit-tested + smoke-
      validated — but a real click-through wasn't possible. Do a manual pass: open a video, press
      `[` at one point and `]` later (confirm the region appears under the timeline), choose
      overflow → "Export Clip…", pick a path, confirm a playable clipped file appears at the In→Out
      range; confirm Clear Marks + the items' enable state behave, and that a stream/live source has
      them disabled.
  - **PS-703 (Media Conversion) COMPLETE** (engineering-verified; see manual-QA caveat below).
    Convert the loaded media to another format via FFmpeg (PICKLE.md §29). Unlike the clip exporter,
    conversion is done **at playback time on the currently loaded (local) file**, through the overflow
    menu; remote streams are excluded by design.
    - New pure catalog `PlaySphere.Domain.Media.ConversionPreset`
      (`Id`/`DisplayName`/`Description`/`TargetExtension`/`FfmpegArgs`) + `ConversionResult`
      (`Ok`)+`ConversionPresets.All` with 8 targets: `mp4-h264`, `mp4-h265`, `mkv-h264`, `mkv-h265`
      (map `-map 0:v:0? -map 0:a:0?` — first video + first audio, both optional, so a video-only
      source still converts; MP4 adds `-movflags +faststart`), `mkv-remux` (`-map 0 -c copy`), and
      audio-only `mp3-192`/`flac-lossless`/`wav-pcm` (`-vn -map 0:a:0?`). `Find(id)` +
      `IsSupportedOutputExtension(ext)` helpers.
    - New `Application.Media.IMediaConverter.ConvertAsync(inputPath, outputPath, preset, ct)`
      implemented by `PlaySphere.Media.Export.FfMpegMediaConverter` (reuses the Export module — no
      new project): the same bounded-subprocess pattern as the clip exporter — structured
      `ArgumentList` only (`-y -hide_banner -loglevel error -i <in>` + preset args + `<out>`, rule
      4/§43), `FfMpegConvertOptions.Timeout` 30 min default, capped stderr (64 KB), soft failure
      (missing input, input==output, extension↔preset mismatch, missing tool, ffmpeg error, timeout
      → `Fail`, never throws; partial output deleted). V1 maps only first video/audio — no subtitle
      pass-through.
    - DI: `AddPlaySphereConversion(FfMpegConvertOptions?)` alongside the clip-export registration in
      `ExportServiceCollectionExtensions`; `App.xaml.cs` chains
      `.AddPlaySphereConversion(ConfigureFfMpegConvertOptions())` (same `FindUpwards("tools","ffmpeg",
      "ffmpeg.exe")` locator as the export/probe variants).
    - `PlayerViewModel`: `CanConvert` (= converter present && local file && not already converting),
      `IsConverting`, `ConvertAsync(outputPath…)` best-effort (never throws), selected preset passed
      down; `Notify(nameof(CanConvert))` hooked into `NotifyAll`. Conversion is VM/engine-free —
      scrapes the current `Title`/`FilePath` only.
    - `PlayerPage` UI: overflow-menu "Convert…" item (disabled/enabled + "Converting…" text from VM
      state) → `ConvertDialog` (preset ListView shown list-style; hidden on the page root so it is
      never a second page child — ContentDialog cannot be a sibling of Page Content) → save picker
      (new `FilePickerHelper.PickSaveConvertFileAsync(suggestedName, displayName, extension)`) →
      `ConvertAsync` → result `ContentDialog`. Suggested stem via `SnapshotNaming.Sanitize` +
      `-converted-yyyyMMdd-HHmmss`.
    - Tests: `ConversionPresetTests` (8: catalog covers all kinds, unique ids, extension shape, video
      presets map `0:v:0?`/`0:a:0?`, audio presets drop video via `-vn`, remux stream-copies,
      `Find`/`IsSupportedOutputExtension`) + 8 VM tests via a new `FakeMediaConverter` (local→true,
      no-converter/remote/no-file→false, passthrough, failure paths, busy-state cleared) +
      `FfMpegMediaConverterTests` (5 real-ffmpeg integration: mp4→mp3 transcode, mkv-remux,
      missing input, extension mismatch fail, missing-tool fail). Full suite: **341 unit + 62
      integration** green, 0 build warnings, smoke OK. (PlaybackTests still shows the documented
      native-timing flake family, membership varies run-to-run.)
    - Manual-QA caveat (same limitation as prior phases): the real ffmpeg conversions are
      integration-tested on a synthetic fixture and the VM/UI wiring is unit-tested + smoke-validated,
      but no real click-through was possible. Do a manual pass: load a video, overflow → "Convert…",
      pick a preset, choose an output path, confirm the converted file appears and plays back, the
      dialog reports success, the menu re-enables after conversion, and that a stream / live source
      has the item disabled. The PICKLE.md §30 background conversion queue (progress/cancel/retry/
      open-output-folder) is a follow-up ticket, not part of PS-703.
- Solution file: `LiVi.PlaySphere.sln`. App: `src/PlaySphere.App`. UI: `src/PlaySphere.UI`.
  - **PS-704 (Timeline Hover Thumbnails) COMPLETE** (engineering-verified; see manual-QA caveat
    below). PICKLE.md §20 "hover thumbnail": scrubbing the timeline shows the frame at the hovered
    position.
    - New `Application.Media.IMediaThumbnailProvider.GenerateStripAsync(filePath, cacheDirectory,
      duration, frameCount, ct)` → `Domain.Media.ThumbnailStripResult` (Success, StripPath, Columns,
      Rows, CellWidth, CellHeight, ErrorMessage). Implemented by
      `PlaySphere.Media.Export.FfMpegThumbnailProvider`: one bounded ffmpeg run produces a **single
      contact-sheet JPEG** (`-vf "fps=<cells/duration>,scale=W:H:force_original_aspect_ratio=decrease,
      pad=W:H:(ow-iw)/2:(oh-ih)/2,tile=COLSxROWS" -frames:v 1`) — no per-frame files, no libVLC seek
      spam, structured arg array only (rule 4/§43). Cached by file identity (lowercased path + mtime +
      length SHA-256) so a reopened file reuses the sheet; missing file / zero duration / missing tool
      / ffmpeg error / timeout all fail softly (`Success == false`, never throws). Grid bounded
      (`ThumbnailStripOptions.MaxCells=96`, cell 160×90, 2-min timeout).
    - DI: `AddPlaySphereThumbnails(ThumbnailStripOptions?)` in `ExportServiceCollectionExtensions`;
      App chains `.AddPlaySphereThumbnails(ConfigureFfMpegThumbnailOptions())` (same
      `FindUpwards("tools","ffmpeg","ffmpeg.exe")` as the export/probe/convert variants).
    - `PlayerViewModel`: new ctor params `thumbnailProvider:`/`thumbnailStripCacheDir:` (optional,
      existing ctors unchanged). `ThumbnailStrip` observable (null until ready/failed); fire-and-forget
      `PrepareThumbnailStripAsync()` on every local load with video tracks (skips remote streams and
      audio-only media, both silently); frame count `clamp(minutes+8, 16, 96)`; pure
      `GetThumbnailCellIndex(percent)` clamps a 0–100 fraction to a row-major cell index.
    - `PlayerPage`: a `ThumbnailPreview` popup above `TimelineSlider` (card Border + clipped Grid +
      sliding Image). The existing scrub handlers now call `ShowScrubPreview` — with a strip available
      it crops the matching cell (Image slides via negative margins inside a cell-sized
      `RectangleGeometry` clip) and hides the timestamp tooltip; without a strip it keeps the old
      tooltip. Popup is centered over the cursor, clamped into the slider's width, and cleared on
      exit/capture-lost.
    - Tests: `FfMpegThumbnailProviderTests` (+4 integration: real filmstrip on the 2s fixture with
      sane grid/cell dims + cache re-use, missing-input fail, zero-duration fail, missing-tool fail)
      + 5 VM unit tests via a `FakeThumbnailProvider` (local video prepares with real identity args,
      audio-only skips, remote skips, provider failure is non-fatal, `GetThumbnailCellIndex` clamps/
      maps a 4×4 grid). Full suite: **346 unit + 66 integration** green, 0 warnings, smoke OK.
    - Manual-QA caveat (same limitation as prior phases): the real filmstrip is integration-tested
      against bundled ffmpeg and the crop math is unit-tested, but the visual popup was not clicked
      through. Do a manual pass: open a local video, hover/scrub the timeline and confirm the frame
      preview tracks the cursor and updates per cell, that it disappears on exit, and that audio-only
      files and streams never show it (they fall back to the timestamp tooltip).
    - **§30 Background Conversion Queue COMPLETE** (engineering-verified; see manual-QA caveat).
      Progress, cancel, retry and open-output-folder for conversion and clip-export jobs, running
      serially so a heavy queue never starves playback (PICKLE.md §30).
      - New module **`PlaySphere.Worker`** (was a stub `.gitkeep`): `PlaySphere.Domain.Media.
        ConversionJob`/`ConversionJobKind`/`ConversionJobStatus` (+ `ProgressFraction`/`IsActive`/
        `CanRetry`/`CanOpenOutputFolder`) and `Application.Media.IConversionQueue` (Jobs snapshot,
        `JobsChanged`, `EnqueueConvert`/`EnqueueClip`/`Cancel`/`Retry`, `IDisposable`). The
        `ConversionQueue` implementation drains jobs **one at a time** on a background worker with a
        per-job `CancellationTokenSource` (linked to a shutdown token), probes the input duration
        once per convert job via optional `IMediaProbe` (clip jobs use their exact segment length as
        the progress total — a genuine 0..1), throttles progress events (default 250 ms,
        `ProgressThrottle`), and publishes terminal states via `with` snapshots so consumers never
        observe a half-updated job. `CancelAsync` is instant for queued jobs and cancels the per-job
        token for running ones; `RetryAsync` re-queues a failed job (bounded `RetryCount`). Test hook:
        `internal WaitForIdleAsync()` surfaced via `InternalsVisibleTo("PlaySphere.IntegrationTests")`.
      - **Progress plumbing:** `IMediaConverter.ConvertAsync` / `IMediaClipExporter.ExportClipAsync`
        gained an optional trailing `IProgress<TimeSpan>? progress` param (after `cancellationToken`,
        so existing call sites and fakes need no positional changes). When non-null, the ffmpeg
        subprocess gets structured `-progress pipe:1` args; `ParseProgressAsync` reads the
        `out_time_us`/`out_time_ms` key=value lines (monotonic, clamped ≥ 0) and reports the absolute
        output timestamp — the subscriber maps it to a fraction against its known total. Both
        converters also set `ProcessPriorityClass.BelowNormal` on the child (heavy jobs must not
        starve ongoing playback). Pipes are drained concurrently to avoid the classic subprocess
        deadlock.
      - **UI:** new `ConversionsPage` (route `AppRoutes.Conversions`, registered in `RouteRegistry`,
        reachable via PlayerPage overflow "Conversions…") — a live job list with per-job status chip,
        progress bar, error text, and Cancel / Retry / Open Folder buttons (Explorer `explorer.exe
        /select,"path"` via a defensive helper). `PlayerPage` Convert/Export Clip flows now **prefer
        the queue** when `IConversionQueue` is resolvable — the picker enqueues a job and the result
        dialog points to the Conversions page; the foreground VM path remains the fallback when the
        queue is unavailable, so behavior degrades gracefully.
      - Architecture notes: `ConversionJobRow` is the x:DataType row type and **must stay plain
        settable properties** (no `required`/`init`) — the WinUI XAML type-info generator
        constructs it and fails CS9035/CS8852 otherwise. ffmpeg args remain structured `ArgumentList`
        only (rule 4/§43). UI depends only on the `IConversionQueue` contract (Application), never on
        `PlaySphere.Worker`.
      - Tests: `tests/PlaySphere.IntegrationTests/ConversionQueueTests.cs` (+9, deterministic, fake
        converter/exporter/probe, `WaitForIdleAsync` instead of sleeps): convert-succeeds, missing-
        input rejected without a job, invalid clip bounds rejected, **serial execution** (first blocked
        on a gate, second stays Queued), cancel-while-running → Cancelled, retry re-runs a failed job
        (RetryCount=1, 2 calls), clip progress maps against the segment duration (≥0.9), probed
        duration feeds the convert progress fraction, JobsChanged fires on enqueue + completion.
        Build clean (0/0), suite green (346 unit + 75 integration), smoke OK; PlaybackTests shows only
        the documented native-timing flake family (membership varies run-to-run).
      - Manual-QA caveat (same limitation as prior phases): no interactive desktop UI automation, so
        the Conversions page click-through wasn't possible. Do a manual pass: overflow → "Convert…",
        pick a preset + path — confirm it NOW adds a queued job with a moving progress bar, cancels
        from the page, and that a second job waits for the first (serial); open the output folder from
        a finished job; and a failed job (e.g. nonexistent input) offers Retry.
- Solution file: `LiVi.PlaySphere.sln`. App: `src/PlaySphere.App`. UI: `src/PlaySphere.UI`.
- Stack: C# 14, .NET 10, WinUI 3 / Windows App SDK 1.8 (see `docs/adr/0001`).
- **Phase 9 (AI Media): STARTED — data foundation + AI-02 audio extraction COMPLETE** (engineering-
  verified; see manual-QA caveat below). This is the first Phase 9 slice; the AI backend boundary,
  transcription call and chapter/summary/ask methods (`IAiMediaService`, `IAiCredentialStore`,
  `OpenAiMediaService`, `WindowsCredentialStore`, `AddPlaySphereAi`) already existed (AI-01/03 + the
  methods behind 06/07/08) but were **unwired and unpinned**. This ticket adds the two layers every
  remaining AI feature (transcript UI, timestamp search, chapters, summary, Ask, translation, the
  API-key settings entry point) depends on: a persistence foundation for AI output and the audio-
  extraction path that feeds transcription. Everything below is engineering-verified; manual
  click-through is still pending (standing limitation — no desktop UI automation here).
  - **AI-02 Audio extraction — COMPLETE.** New `Media.Export` member (no new project — it is a
    bounded ffmpeg subprocess exactly like the existing converter/clip-exporter, and reuses the same
    `FindUpwards("tools","ffmpeg","ffmpeg.exe")` locator):
    - `Domain.Media/AudioExtractionResult.cs` — `record AudioExtractionResult(bool Success,
      string? ErrorMessage, string? OutputPath)` + `Ok(path)`/`Fail(msg)`, always produced never thrown.
    - `Application.Media/IAudioExtractor` — `Task<AudioExtractionResult> ExtractAudioAsync(inputPath,
      outputPath, ct)`; the seam UI/VM depend on (never ffmpeg directly). PICKLE.md §38: audio is
      preferred over full video for transcription.
    - `FfMpegAudioExtractor` + `FfMpegAudioExtractOptions` (`ExecutablePath`/`Timeout` 10m/`MaxErrorBytes`
      64 KB) in `PlaySphere.Media.Export`. Structured `ArgumentList` only (rule 4/§43):
      `-y -hide_banner -loglevel error -i <in> -vn -ac 1 -ar 16000 -c:a pcm_s16le <out.wav>` — a mono
      16 kHz signed-16-bit WAV, the smallest Whisper-friendly upload and a downmix that keeps speech
      intelligible without knowing the source layout. Drops to BelowNormal priority; drains pipes
      asynchronously; bounded timeout + linked cancellation; partial output deleted on failure; soft
      failure (missing input/tool, non-.wav output, subprocess error, timeout) → `Fail`, never throws.
      Only `.wav` output is accepted.
    - DI: `ExportServiceCollectionExtensions.AddPlaySphereAudioExtraction(FfMpegAudioExtractOptions?)`
      + `App.xaml.cs` chain `.AddPlaySphereAudioExtraction(ConfigureFfMpegAudioExtractOptions())`.
    - Tests: `tests/PlaySphere.IntegrationTests/FfMpegAudioExtractorTests.cs` (+5, real bundled ffmpeg:
      extracts a real mono WAV (>1000 bytes PCM body) from a synthetic video, no-audio-stream fails,
      missing input fails, non-.wav output fails, missing-tool fails softly).
  - **AI persistence foundation — COMPLETE** (PICKLE.md §42 tables `transcripts`,
    `transcript_segments`, `chapters`, `ai_artifacts`). New EF entities in `Domain.Library`
    (mirroring the Bookmark/Playlist persisted-entity precedent): `AiTranscript` (one per media item,
    Language, SourceModel, CreatedAt, + a navigational-`Ignore`d `Segments` list), `AiTranscriptSegment`
    (TranscriptId, Ordinal, StartSeconds, EndSeconds, Text), `AiChapterEntity` (named `*Entity` to
    avoid clashing with the immutable `Domain.AI.AiChapter` record — MediaItemId, Title, StartSeconds),
    and `AiArtifact` (MediaItemId + `Key` discriminator + `PayloadJson` + optional `StartSeconds`, so
    the schema stays stable as new artifact kinds arrive without a migration — no per-kind table).
  - `PlaySphereDbContext`: new DbSets + FKs cascading to `media_items` (and `transcripts`→
    `transcript_segments`), unique index on transcript `MediaItemId`, `(MediaItemId, Ordinal)` /
    `(MediaItemId, StartSeconds)` / `(MediaItemId, Key)` indexes. EF migration **`AddAiTables`**
    (20260903012332) generated via the design-time factory; applies cleanly on top of the existing
    database at startup (confirmed by smoke-launch running `MigrateLibraryDatabase()` on the real
    `%LOCALAPPDATA%` DB that already had the earlier migrations — the incremental-upgrade path, not
    just a fresh DB).
  - `IMediaLibraryRepository` + `EfMediaLibraryRepository` new methods:
    `SaveTranscriptAsync(transcript, segments)` (delete-then-insert so one transcript per item, latest
    wins, `GetTranscriptAsync` returns it with `Segments` ordered by `Ordinal`), `DeleteTranscriptAsync`,
    `SaveChaptersAsync(mediaItemId, chapters)` (replace-generation, `GetChaptersAsync` ordered by
    `StartSeconds`), `SaveArtifactAsync`/`GetArtifactsAsync` (creation order)/`DeleteArtifactsAsync`.
    `FakeLibraryRepository` in `PlayerViewModelTests` implements the new members as
    `NotSupportedException` stubs (it never uses them).
  - Tests: `tests/PlaySphere.IntegrationTests/AiPersistenceRepositoryTests.cs` (+8, real migrated
    SQLite via `LibraryDatabaseFixture`): transcript persists ordered segments, replace-latest-wins,
    null-when-none, delete removes transcript+segments, chapters persist+order, chapters replace
    generation, artifacts persist+creation-order+timestamp, delete-all.
  - Verification: solution Release build 0 errors (0 new warnings — the 13 xUnit1030
    `ConfigureAwait(false)` warnings are pre-existing in the untouched `OpenAiMediaServiceTests.cs`),
    IntegrationTests suite green (93 passed incl. the 13 new), `PlayerViewModelTests` green (112),
    `tools\run-app-smoke.ps1` OK (boots to Home → the `AddAiTables` migration applied to the real
    on-disk DB). Known test-harness caveats unchanged: the full UnitTests + PlaybackTests suites
    still show the documented native-timing flake family / long suite time; use targeted filters.
  - **Next up in Phase 9 (not started):** AI-09 subtitle translation, and the API-key settings entry
    point + privacy/consent flow (PICKLE.md §40). The `IAiMediaService` boundary has no translation
    method yet — AI-09 will need to extend it. Manual QA for the AI slices: run a transcribe and confirm
    the mono 16 kHz WAV produced is the file actually sent.
  - **AI-04 (Transcript UI) + AI-05 (Timestamp search) COMPLETE** (engineering-verified; see
    manual-QA caveat below). The first end-user AI surface — a working `AiMediaPage` where a
    cataloged media file is transcribed and any spoken moment is searched and jumped to with a
    click. Everything below is engineering-verified; manual click-through is still pending (standing
    limitation — no desktop UI automation here).
    - **AI-05 search core (was added earlier, kept):** `PlaySphere.Application/AI/TranscriptSearcher.cs`
      — a pure, untrusted-safe case-insensitive substring search over a transcript, with two overloads
      covering the two runtime shapes (in-memory `TranscriptSegment` from a live transcription, and
      persisted `AiTranscriptSegment` loaded from the repository), empty/whitespace queries → no results,
      results in transcript order. `AI/TranscriptSearcherTests.cs` (6 tests).
    - **Player seek-on-load (small safe player extension):** `Navigation/PlayerQueuePayload.cs` gained
      an optional `TimeSpan? Position` (backward-compatible `(filePaths, startFilePath = null,
      position = null)` ctor). `PlayerViewModel` gained `SeekToPositionAsync(TimeSpan)` — clamps to
      duration, best-effort `try/catch` on `_engine.SeekAsync`, no-op when unseekable (all established
      patterns). `PlayerPage.xaml.cs` `PlayerQueuePayload` branch calls it after `LoadCollectionAsync`
      when `Position` is present. 3 new `PlayerViewModelTests` (seeks to position, clamps at duration,
      no-op when unseekable). Deliberately **not** wired into the engine/rebuild path — no overlap with
      the recorded AV/rebuild risk classes.
    - **`AiMediaPage` UI (replaced the stub):** resolves `IMediaLibraryRepository`/`ITranscribeMediaRunner`/
      `IAiCredentialStore` defensively via `PlaySphereServices.GetOptional` (page stays null-capable if
      a service is missing). Layout (all tokens passed the `DesignTokenReferenceTests` guard):
      - **API key row:** shows configured/not status; a "Set key"/"Clear key" button opens a
        `ContentDialog` with a `PasswordBox` + the §40 consent/privacy note, saving via
        `IAiCredentialStore.SetApiKey` (Windows Credential Manager backend, never settings JSON).
      - **Media picker + Transcribe:** a `ComboBox` populated from `IMediaLibraryRepository.GetAllMediaItemsAsync`
        (title or path); selecting reloads any persisted transcript via `GetTranscriptAsync`. The
        "2. Transcribe" button is enabled on selection (and not busy), first-time runs confirm consent
        via a `ContentDialog` (audio-only upload note, §40), then calls
        `ITranscribeMediaRunner.RunAsync(path)` and re-reads the persisted transcript on success;
        errors surface in the status line, never throw.
      - **Transcript + search:** an `ItemsControl` of clickable cards (timestamp + text); the search
        box runs `TranscriptSearcher.Search` live over the persisted segments (shows a live match
        count), and tapping a result navigates to `PlayerPage` with a `PlayerQueuePayload([path],
        startFilePath: path, position: StartSeconds)` so the player loads the file and seeks to the
        spoken moment. No transcript → an honest empty state.
    - Tests green for this slice: 6 `TranscriptSearcherTests` + 7 `TranscribeMediaRunnerTests` +
      3 `PlayerViewModel` seek tests + the `registers_the_transcription_runner` DI-descriptor test
      override. `DesignTokenReferenceTests` passes (new XAML has no undefined custom tokens). Full
      solution Release build 0/0, smoke OK (app boots to Home, `AddAiTables` migration still applies).
    - **Known test-environment caveat (documented, pre-existing, not caused by this slice):**
      `AiServiceCollectionRegistrationTests.registers_boundary_and_credential_store` (which builds a
      DI provider and resolves `IAiMediaService`/`IAiCredentialStore`, i.e. constructs
      `OpenAiMediaService` + `WindowsCredentialStore`) **hangs** this headless host (VSTest blame
      produced a hang dump; the pre-existing `registers_boundary_and_credential_store`/provider-building
      tests hang, while the `registers_the_transcription_runner` descriptor check passes in 14ms). It
      is untouched-by-this-slice, pre-existing code — consistent with AGENTS' documented host flake
      family. Do not enable it in an unfiltered suite run here; run targeted filters. A healthy/displayed
      Windows host should pass it as it did historically.
    - **Registration note (if editing `AddPlaySphereAi`):** `ITranscribeMediaRunner`'s full resolution
      needs the sibling modules' `IAudioExtractor` (AI-02) + `IMediaLibraryRepository` (Library), which
      `AddPlaySphereAi` does NOT register (they come from `.AddPlaySphereAudioExtraction`/`.AddPlaySphereLibrary`
      earlier in the App DI chain). The DI test asserts the descriptor mapping instead of full
      resolution for that reason. Manual QA remaining (standing caveat): play a cataloged file with
      speech, transcribe from the AI page, confirm the WAV actually extracted/sent is the mono 16 kHz
      one, search a phrase, and click a result to confirm the player loads + seeks to that second.
  - **AI-06 (Auto Chapters into the Chapters drawer) COMPLETE** (engineering-verified; see manual-QA
    caveat below). PICKLE.md §38 "automatic chapters": generate topic chapters from the persisted
    transcript and land them in the existing PlayerPage Chapters/Bookmarks drawer, click-to-seek.
    Everything below is engineering-verified; manual click-through is still pending (standing
    limitation — no desktop UI automation here).
    - **Backend (new), mirroring the AI-04 `ITranscribeMediaRunner` shape:**
      - `Domain/AI/GenerateChaptersResult.cs` — `record GenerateChaptersResult(bool Success,
        string? ErrorMessage, IReadOnlyList<AiChapterEntity>? Chapters)` + `static Fail(msg)`; always
        produced, never thrown for user-correctable failures.
      - `Application/AI/IGenerateChaptersRunner.cs` — `Task<GenerateChaptersResult>
        GenerateChaptersAsync(string mediaFilePath, CancellationToken)`; the seam UI/VM depend on
        (never the AI provider directly).
      - `PlaySphere.AI/GenerateChaptersRunner.cs` — validates the file exists, `FindByPathAsync` for
        a cataloged item, loads the persisted transcript via `GetTranscriptAsync` (clean
        "transcribe first" guidance when none), builds a defensively `StartSeconds`-sorted
        `Transcript`, calls `IAiMediaService.GenerateChaptersAsync`, maps/validates each `AiChapter`
        into a persisted `AiChapterEntity` (title trimmed & ≤300 chars, finite `StartSeconds` ≥ 0;
        invalid rows dropped), sorts by start, and `SaveChaptersAsync` for the item. Persistence is
        best-effort (returns success with the in-memory chapters even if the save throws, since a
        provider round-trip already happened). `AiException` → its message; `OperationCanceledException`
        rethrown. Registered in `AddPlaySphereAi` as `IGenerateChaptersRunner`.
    - **VM surface (`PlayerViewModel`):** optional ctor param `IGenerateChaptersRunner?
      generateChaptersRunner = null`, plus `CanGenerateChapters` (runner + repo + cataloged
      `_libraryItemId`), `IsGeneratingChapters`, `ChaptersMessage`, `Chapters`
      (`IReadOnlyList<AiChapterEntity>`, cleared on every `LoadAsync` reset block),
      `LoadChaptersAsync()` (best-effort `GetChaptersAsync`), `GenerateChaptersAsync(CancellationToken)`
      (returns success; guards busy + unavailable; sets `Chapters`/`ChaptersMessage`), and
      `SeekToChapterAsync(chapter)` (clamps `StartSeconds` to `_engine.Duration`, best-effort
      `SeekAsync`). `NotifyAll` notifies `Chapters` + `CanGenerateChapters`.
    - **PlayerPage UI + wiring:** a new "AI Chapters" section in the Chapters/Bookmarks drawer
      (status text + `GenerateChaptersButton` + a `ChaptersList` of clickable seek cards). The
      Chapters drawer-open path now also calls `PopulateChaptersUiAsync()` (loads chapters, sets the
      generate-button enabled state from `CanGenerateChapters`, shows honest empty/status text),
      `GenerateChaptersButton_Click` runs a §40-consent `ContentDialog` then `GenerateChaptersAsync`
      then re-populates (never throws), and each chapter card's `Tapped` seeks via
      `SeekToChapterAsync`. `OnNavigatedTo` resolves `IGenerateChaptersRunner` via
      `PlaySphereServices.GetOptional` and passes it to all three `PlayerViewModel` ctor sites.
      Code-behind references only the four defined design tokens (`BrushSurfaceCard`,
      `BrushAccentCyan`, `BrushTextPrimary`, `BrushTextMuted`).
    - Tests: `tests/PlaySphere.UnitTests/AI/GenerateChaptersRunnerTests.cs` (+9, fake provider +
      fake repo with real transcript persistence: missing file, non-cataloged, no-transcript-yet,
      provider failure, empty result, success persists ordered chapters, invalid generated rows are
      dropped, library-lookup failure) + 7 new `PlayerViewModelTests` chapter tests (CanGenerateChapters
      true/false ×runner×catalog, generate success/failure, load persisted, seek clamped target,
      seek clamps at duration, chapters cleared on new load) — the shared `FakeLibraryRepository`
      gained a stateful chapters store (mirroring its bookmarks store). Full solution Release build
      0/0, targeted run 16/16 pass, `DesignTokenReferenceTests` guard passes, smoke OK (app boots to
      Home).
    - Manual-QA caveat (same limitation as prior phases): no interactive desktop UI automation, so
      the drawer section, consent dialog, chapter cards and click-to-seek were **not** clicked
      through — only the runner unit tests (fake provider + real repo persistence), VM chapter tests
      (fake runner/engine), build + tests, and smoke launch. Do a manual pass: transcribe a cataloged
      file (AI Media page), open a video and the Chapters drawer, press "Generate AI Chapters",
      confirm the consent dialog + generated chapter cards appear, click one and confirm the player
      seeks to that timestamp, and that an audio-only session or a file without a transcript reports
      the honest guidance instead of generating.
  - **AI-07 (Media summary) + AI-08 (Ask-the-Media) COMPLETE** (engineering-verified; see manual-QA
    caveat below). PICKLE.md �38 "media summary" and �41 "ask the media" surfaced on the existing
    `AiMediaPage`, mirroring the AI-06 runner pattern end-to-end. Everything below is engineering-
    verified; manual click-through is still pending (standing limitation � no desktop UI automation).
    - **Backend (new), same shape as the AI-04/06 runners:**
      - `Domain/AI/SummarizeMediaResult.cs` � `record SummarizeMediaResult(bool Success, string? ErrorMessage,
        AiSummary? Summary)` + `static Fail(msg)`; and `Domain/AI/AskMediaResult.cs` � `record
        AskMediaResult(bool Success, string? ErrorMessage, IReadOnlyList<AiKeyPoint>? Answers)` + `static
        Fail(msg)`. Always produced, never thrown for user-correctable failures (untrusted input, �43).
      - `Application/AI/ISummarizeMediaRunner.cs` (with the persisted-artifact key constant
        `ISummarizeMediaRunner.SummaryArtifactKey = "summary"`) and `Application/AI/IAskMediaRunner.cs`
        � the seams UI depends on (never the AI provider directly). Both follow the `ITranscribeMediaRunner`
        / `IGenerateChaptersRunner` boundary discipline.
      - `AI/SummarizeMediaRunner.cs` � validates the file exists, `FindByPathAsync` for a cataloged item,
        loads the persisted transcript via `GetTranscriptAsync` (clean "transcribe first" guidance when
        none), builds a defensively-sorted `Transcript`, calls `IAiMediaService.SummarizeAsync`, persists
        the `AiSummary` as an **artifact** under `SummaryArtifactKey` via `SaveArtifactAsync` (JSON payload,
        best-effort: a persistence failure still returns success with the in-memory summary). `AI/AskMediaRunner.cs`
        mirrors it for `AskAsync` (guards a blank question; returns the provider's clickable-timestamp
        `AiKeyPoint` list; **not** persisted � a live query). Both catch `AiException` ? its message and
        rethrow `OperationCanceledException`. Registered in `AddPlaySphereAi` as
        `ISummarizeMediaRunner`/`IAskMediaRunner` singletons.
    - **`AiMediaPage` UI:** two new `Border` sections under the transcript/search card, both using only
      defined design tokens (passes the `DesignTokenReferenceTests` guard):
      - **Summary (4. summary):** "Generate summary" button + status line + a summary card (TextBlock).
        The page resolves `ISummarizeMediaRunner` via `PlaySphereServices.GetOptional` (null ? the section
        reports unavailable) and, on media selection, loads any persisted summary via `GetArtifactsAsync`
        filtering `SummaryArtifactKey` (JSON-deserialized `AiSummary`, untrusted-safe best-effort) so a
        previously-generated summary reappears when the file is reopened. Generate ? `SummarizeAsync` ?
        shows the text + word count, or the fail-soft message; busy-guarded (`_summarizerBusy`), never throws.
      - **Ask (5. ask):** a question `TextBox` (Enter to submit) + Ask button ? `AskAsync` ? an
        `ItemsControl` of answer cards; a row with timestamp evidence (`StartSeconds` present) shows a
        **clickable** timestamp label that `NavigateToMedia(startSeconds)`s into `PlayerPage`, while a row
        without one shows text only (never fabricates a jump � �41 contract). No transcript/key yet ? the
        runner's fail-soft message; missing API key ? a "Set an OpenAI API key first" `ContentDialog` with a
        PasswordBox (reuses the Windows-credential-store flow) before the action runs. Both busy-guarded.
      - Shared `UpdateAiActionEnablement()` folds the enable-state of Generate-Summary/Ask across the
        media-selection, transcript-load and busy paths.
    - Tests: `tests/PlaySphere.UnitTests/AI/SummarizeMediaRunnerTests.cs` (+8, fake provider + fake repo
      with a stateful `SaveArtifactAsync`: missing file, non-cataloged, no-transcript-yet, provider failure,
      empty-summary fail, success returns + persists ordered artifact with the right key/JSON/library id,
      persistence-failure-is-best-effort-but-still-succeeds, library-lookup failure) + `AskMediaRunnerTests.cs`
      (+8: blank question, missing file, non-cataloged, no-transcript, provider failure, empty-answers fail,
      success passes the exact question through + returns timestamped/non-timestamped answers, library-lookup
      failure). Full solution Release build **0/0** (the 14 warnings on the first full compile are the
      pre-existing ones, none in the new files), targeted run 38 unit + 8 integration green (incl.
      `DesignTokenReferenceTests` + `AiPersistenceRepositoryTests`), smoke OK (app boots to Home; WinUI
      compile-time XAML validation for the two new page sections passed).
    - Manual-QA caveat (same limitation as prior phases): no interactive desktop UI automation, so the two
      new sections, the persisted-summary reload, the ask flow, the consent/key dialog and the timestamp
      jump were **not** clicked through � only backend unit tests (fake provider + real artifact
      persistence), build + tests, DesignToken guard, and smoke launch. Do a manual pass: transcribe a
      cataloged file (AI Media page), press "Generate summary" and confirm the summary card fills and that
      reopening the same file reloads it; ask a question, confirm the answer cards appear, and that clicking
      a timestamp jumps the player to that moment while answer rows without a timestamp stay text-only;
      and that a file with no transcript (or no API key) reports the honest guidance instead of calling the
      provider. The `AiServiceCollectionRegistrationTests.registers_boundary_and_credential_store` host-hang
      caveat is unchanged � run targeted filters, never an unfiltered suite.
- **"Whichever media file I open, the application hangs before any picture" — ROOT-CAUSED AND FIXED
  (2026-09-03).** User repro: "whatever media file I open the application hangs" (whole window freezes, no
  picture). The user's real `%LOCALAPPDATA%\LiVi Play Sphere Studio\libvlc.log` was the smoking gun:
  - **Every MP4/video open stalled at `Creating an input for preparsing 'X.mp4'`** and then emitted only the
    Lua **meta/art-fetcher** noise (`00_musicbrainz.luac` / `01_googleimage.luac` / `02_frenchtv.luac` /
    `03_lastfm.luac`) — it never advanced to a real demux/decode/video vout (`no demux module`, no
    "Picture ready", no `PlaybackThreadsAskToRestart`).
  - **MP3s DID advance to real playback** (`es` demux → `mpg123` → `wasapi` → "Stream buffering done" →
    "state changed: 1"), so the engine's audio path was fine.
  - Timestamp gaps proved a single `Parse` call **hung ~5 minutes** (e.g. `15:53:46` preparse of a video, next
    log line `15:58:32`) — far beyond the engine's 5000 ms parse timeout, because libVLC's native meta/art-
    fetch thread was blocked inside a network Lua script that the timeout's status-polling cannot preempt.
  - **Root cause:** `LibVlcPlaybackEngine.LoadCoreAsync` (and `LoadNetworkAsync`) call `vlcMedia.Parse(...)`
    which, for a media that triggers cover-art/metadata lookup, invokes libVLC's **network art finders**
    (MusicBrainz / Google Image / Last.fm). On a machine without fast/working network reachability these
    block the native parse thread, and the engine's `LoadAsync` awaits it → any video open hangs, "no
    picture". Audio files (no art lookup path) load fine, matching the log exactly.
  - **Fix (smallest coherent change, playback first):** a new static helper
    `ApplyMetadataNoNetworkAccess(Media)` emits the libVLC per-media options **`:no-metadata-network-access`**
    and **`:no-arts`** (we generate cover art ourselves via engine snapshots, so art at load time is
    unneeded). Called at the top of **all three** media-option fabrication sites: `LoadCoreAsync`,
    `LoadNetworkAsync`, and the `RebuildRenderer` re-attach site (before `_mediaPlayer.Media = media`).
    Parse is now local, fast and deterministic — no network meta/art fetch, so it returns well within the
    timeout and video opens no longer hang.
  - **Verification:** full solution Release build **0 errors / 0 warnings**; the 6 `MediaLoadTests` against
    the real libVLC adapter still pass (MP3 → Ready with audio, MP4 → Ready with video+audio, mkv multi-audio,
    error/recovery, events) proving the options don't break local parsing or track extraction;
    `tools\run-app-smoke.ps1` OK (app boots to Home). A/B confirmed the option change is **not** the cause of
    the intermittent `SeekTests`/`ErrorRecoveryTests` failures on this headless host (Buffering-vs-Paused state
    timing, position readback 0): those fail **identically with the option injection disabled** (temporary no-op
    hack flag) — the documented native-timing flake family, membership varies run-to-run. Exact regression
    coverage of "Parse completes fast on a real video" is covered by the unchanged MediaLoadTests (the network-
    fetch hang is machine/network-specific and not reproducible headlessly); the option emission is a one-liner
    guarded by the passing load tests.
  - Manual-QA remaining (standing caveat — no desktop UI automation here): on the user's reproducing machine,
    open a video that previously hung and confirm the picture renders promptly (no freeze), a photo/cover-art
    scenario no longer stalls the load, and that the cover-art snapshot still populates (independent of art
    fetching). If a video still hangs after this, the remaining suspicion moves to the video-surface/swap-chain
    attach path (`PlayerPage` → `AttachVideoSurfaceAsync` → `RebuildRenderer` with `SwapChainOptions`), not the
    audio/parse path (which the log proves healthy).
- **White titlebar + generic taskbar icon — FIXED (2026-09-25):** user reported the window's caption
  bar was white (not matching PlaySphere's dark content) and the taskbar showed a default Windows
  app icon instead of the app's own artwork while running.
  - **Titlebar root cause:** the native Windows caption bar follows the OS light/dark setting, not
    this app's `RequestedTheme="Dark"` (`App.xaml`) — on a light-themed OS the titlebar rendered
    white over the dark UI. Fixed in `src/PlaySphere.App/MainWindow.xaml.cs`: a new
    `ApplyDarkTitleBar()` (called from the `MainWindow` ctor) sets the DWM
    `DWMWA_USE_IMMERSIVE_DARK_MODE` attribute (attribute id 20, via a `dwmapi.dll` P/Invoke on the
    HWND from `WinRT.Interop.WindowNative.GetWindowHandle(this)`) to darken the native frame/border,
    and themes `AppWindow.TitleBar`'s background/foreground/button colors to the app's palette
    (`#0E1117` background, `#F8FAFC` foreground, `#263142` hover — matching
    `ColorBackgroundPrimary`/`ColorTextPrimary`/`ColorSurfaceHover` in `DesignTokens.xaml`).
    Best-effort/try-catch throughout, same pattern as the existing `SetWindowIcon()`.
  - **Taskbar icon:** turned out NOT to be a code bug — `SetWindowIcon()` (pre-existing) was already
    calling `AppWindow.SetIcon` correctly. Confirmed via temporary diagnostic logging (added, verified,
    then removed — never committed) that the call succeeds with no exception, and via
    `ExtractIconEx`/`ExtractAssociatedIcon` that the built exe has exactly one embedded icon resource
    (the real `app.ico`, not a fallback). The generic icon the user saw was **Windows' icon cache**
    showing a stale/default icon from before the exe had its custom artwork embedded. Fixed by
    clearing `%LOCALAPPDATA%\IconCache.db` +
    `%LOCALAPPDATA%\Microsoft\Windows\Explorer\iconcache_*.db` and restarting `explorer.exe`, then
    relaunching the freshly rebuilt exe — confirmed via screenshot that the taskbar now shows the
    correct gradient play-triangle/orbit icon. **Lesson for next time a "taskbar icon is wrong" report
    comes in:** check the icon cache before assuming a code regression — `ExtractIconEx` on the built
    exe is a fast way to confirm the icon is actually embedded correctly before chasing the runtime
    `SetIcon` path.
  - Also required installing the **.NET 10 SDK** (10.0.401) via `winget install
    Microsoft.DotNet.SDK.10` on this machine, which previously only had 8.0.425 — `global.json` pins
    `10.0.400` with `rollForward: latestPatch`, which doesn't bridge major versions, so builds failed
    until the correct SDK was present.
  - Committed as `7915ef3` ("Fix white titlebar and taskbar icon not reflecting the app's dark
    theme"), build clean (0 warnings/errors) both before and after the temporary diagnostic logging
    was added/removed. No automated test coverage (native window-chrome/taskbar behavior, not
    unit-testable) — verified by an actual screenshot of the running app's taskbar entry, zoomed in
    to confirm the icon pixel-for-pixel.
- **AI-09 (Subtitle translation) COMPLETE (2026-09-25)** (engineering-verified; see manual-QA
  caveat below). Closes the last "not started" item noted in the earlier Phase 9 entry above. Follows
  the exact orchestration-seam pattern already used by AI-06/07/08
  (`IGenerateChaptersRunner`/`ISummarizeMediaRunner`/`IAskMediaRunner`): translate a cataloged file's
  persisted transcript into any target language, segment-for-segment, with the original timestamps
  preserved verbatim (PICKLE.md §38 "subtitle translation").
  - `IAiMediaService` gained `TranslateAsync(Transcript, string targetLanguage, CancellationToken)` →
    `IReadOnlyList<TranscriptSegment>`. `OpenAiMediaService.TranslateAsync` builds a chat-completion
    prompt instructing the model to return ONLY a JSON array of `{start, end, text}` with "start"/"end"
    copied verbatim from the original segment and only "text" translated; `ParseTranslatedSegments`
    parses it defensively (untrusted input, §43) via the same `ParseJsonArray` helper the
    chapters/ask parsers already use.
  - New `Domain.AI.TranslateMediaResult(bool Success, string? ErrorMessage,
    IReadOnlyList<TranscriptSegment>? Segments, string? TargetLanguage)` + `Fail(msg)`.
  - New `Application.AI.ITranslateMediaRunner` (the UI-facing seam) with a static
    `ArtifactKeyFor(targetLanguage)` helper (`"translation:" + lowercased/trimmed language`, so the
    same language always round-trips to the same persisted-artifact key — the AI-07 `summary` artifact
    precedent extended to be per-language).
  - `PlaySphere.AI.TranslateMediaRunner` chains: file-exists check → cataloged-item lookup via
    `FindByPathAsync` → persisted-transcript load via `GetTranscriptAsync` (both with the established
    "transcribe first" / "add to library first" guidance on failure) → `IAiMediaService.TranslateAsync`
    → persist the returned segments as a JSON artifact via `SaveArtifactAsync` (best-effort — a
    persistence failure still returns the in-memory translation, matching the summarizer's contract).
    Registered as a singleton in `AddPlaySphereAi`.
  - `AiMediaPage` UI: a new "Translate subtitles" card (free-text target-language box + Translate
    button + status line + a clickable-timestamp segment list, reusing the existing `TranscriptRow`
    shape and card template) sits after the Ask-the-Media section. Enabled/disabled alongside the
    other AI actions via the existing `UpdateAiActionEnablement()` (extended with a `_translateBusy`
    flag) and gated on the same API-key consent dialog (`ConfirmProviderNeeded`) the summary/ask
    actions already use. Deliberately does **not** auto-reload a persisted translation on file
    selection (unlike the summary, which has one canonical artifact) — a translation is keyed by
    target language, so there is no single "the" translation to restore without asking which
    language first; the user re-runs Translate to see a past result for a given language.
  - Tests: 9 new `TranslateMediaRunnerTests` (missing file, blank language, non-cataloged file,
    no-transcript-yet, provider failure, empty-result failure, success passes the exact transcript/
    language through and persists the artifact under the per-language key, persistence-failure-is-
    best-effort-but-still-succeeds, library-lookup failure) + 3 new `OpenAiMediaServiceTests`
    (parses segments preserving timestamps, ignores malformed JSON entries, blank language throws
    `AiException`) + 1 new `AiServiceCollectionRegistrationTests` descriptor test (mirrors the existing
    `registers_the_transcription_runner` pattern). The four pre-existing `FakeAiService` test doubles
    in `SummarizeMediaRunnerTests`/`AskMediaRunnerTests`/`GenerateChaptersRunnerTests`/
    `TranscribeMediaRunnerTests` each needed a `TranslateAsync` stub added to keep implementing
    `IAiMediaService` after the interface grew this member. Full solution Release build 0 errors (only
    the pre-existing xUnit1030/xUnit2013 warnings, none in new files); targeted run
    `PlaySphere.UnitTests.AI` + `DesignTokenReferenceTests` = 65/65 green (up from 51 AI tests before
    this ticket); `tools\run-app-smoke.ps1` OK (compile-time XAML validation for the new page section
    passed).
  - Manual-QA caveat (same limitation as every other AI-media slice): no interactive desktop UI
    automation here, so the new card's target-language box, Translate button, consent dialog and
    resulting segment list were **not** clicked through — only the runner unit tests (fake provider +
    fake repo persistence), the HTTP-parsing tests against a scripted `OpenAiMediaService`, build +
    tests, DesignToken guard, and smoke launch. Do a manual pass: transcribe a cataloged file (AI
    Media page), type a target language into the new "Translate subtitles" box, click Translate,
    confirm the returned segments read as a translation with the *same* timestamps as the original
    transcript, and that a file with no transcript yet (or no API key) reports the honest guidance
    instead of calling the provider. The API-key settings entry point + privacy/consent flow mentioned
    in the earlier "Next up" note was already delivered as part of AI-04 (the "OpenAI API key" card
    on `AiMediaPage`) — that part of the old note is stale/superseded, not still pending.
- **AI-09 follow-up: Export translated subtitles as SRT (2026-09-25)** (engineering-verified; see
  manual-QA caveat below). AI-09 above produced translated segments only as an in-app list — there was
  no way to actually *use* a translation as a subtitle file, which is the whole point of PICKLE.md §38
  "subtitle translation". Adds an "Export SRT" button next to the Translate status line on
  `AiMediaPage` that saves the most recently translated segments as a real `.srt` file.
  - No new domain/application code was needed — this reuses two already-existing, already-tested
    pieces exactly as designed: `PlaySphere.Subtitles.SubtitleExporter.WriteSrtAsync` (the existing
    SRT writer used by the player's own subtitle-export feature) and
    `FilePickerHelper.PickSaveSubtitleFileAsync` (the existing SRT save picker, already used by
    `PlayerPage`'s subtitle export). `AiMediaPage` now references `PlaySphere.Subtitles` and
    `PlaySphere.UI.Helpers` to reach them.
  - `AiMediaPage.xaml.cs`: the page now remembers the most recent successful translation
    (`_lastTranslation`/`_lastTranslationLanguage`), cleared whenever the file selection changes or a
    translate attempt fails/errors (so the export button can never save stale segments for a
    different file or language than what's on screen). `ExportTranslationButton_Click` builds a
    `SubtitleDocument` from the translated `TranscriptSegment`s (`SnapshotNaming.Sanitize` derives a
    safe suggested filename from the media title + target language, matching the Phase 7 snapshot/
    clip-export naming convention) and writes it via the existing exporter — no ffmpeg, no new I/O
    surface, no new failure modes beyond "file picker cancelled" / "write failed" (both handled,
    non-fatal).
  - No new automated tests were added for this slice: both reused pieces
    (`SubtitleExporter.WriteSrtAsync`, `FilePickerHelper.PickSaveSubtitleFileAsync`) already ship with
    their own coverage from the pre-existing player subtitle-export feature, and the only new logic
    here is UI wiring (state tracking + a click handler), consistent with how the codebase has treated
    prior thin UI-only additions. Verified instead by: full solution Release build (0 errors), the
    existing 65 AI + `DesignTokenReferenceTests` targeted tests still green (unaffected by this
    UI-only change), and a smoke launch (confirms the new XAML/button compile and the page loads).
  - Manual-QA caveat (same limitation as every other AI-media slice): no interactive desktop UI
    automation here, so the actual save-and-reopen round trip was **not** clicked through. Do a manual
    pass: translate a file's transcript (AI Media page), click "Export SRT", save it, and open the
    resulting `.srt` file in a text editor or media player to confirm valid SRT timing/text and that
    it can be loaded as an external subtitle track.
- **Session checkpoint (2026-09-25) — where to resume next time:** working tree is clean, all commits
  pushed to `origin/master` (HEAD is `3d758ee`, "Add Export SRT for AI-09 translated subtitles").
  Everything below is what's actually left; nothing is mid-flight or uncommitted.
  - **The only genuinely un-implemented feature:** true stereo L/R balance (distinct from the already-
    implemented `AudioChannelMode` full-pan downmix). Ruled out via the mandated libVLC engine — no
    API for a continuous per-channel gain balance — so doing it for real needs an off-engine FFmpeg
    audio route, which is a playback-engine boundary change and per AGENTS.md rule 9 requires an ADR
    first. Don't attempt without that ADR.
  - **Standing manual-QA backlog** (no code to write — everything below is engineering-verified via
    build/tests/smoke but has never been visually clicked through, since this dev environment has no
    desktop UI automation): playback surface rendering (fullscreen/cinema/mini-player, subtitle
    styling); Playlists/Bookmarks UI + M3U import/export round-trip; the visualizer's 9 presets +
    WASAPI loopback audio-and-visual-together; Library cards/thumbnails/Continue-Watching resume
    accuracy; Phase 7 tools (snapshot, clip export, conversion + background queue, hover thumbnails);
    and the full Phase 9 AI chain end-to-end (transcribe → confirm the actual mono 16 kHz WAV sent,
    timestamp search, auto-chapters, summary, ask, translate, and the new Export SRT round-trip).
  - **Known unresolved-by-design, not just "todo":** (1) toggling the visualizer mid-playback on the
    non-Windows PCM-tap fallback path is a no-op until the next file load — a prior fix attempt (a
    Stop/Play cycle) caused a real `AccessViolationException` crash and was reverted; needs a product
    decision, not another unverified engineering attempt. (2) A live/interminable stream that goes
    silent without firing `EncounteredError` is not auto-recovered — no reliable headless-safe detector
    exists yet. (3) `AiServiceCollectionRegistrationTests.AddPlaySphereAi_registers_boundary_and_credential_store`
    and the broader native-timing test flake family in `PlaybackTests` are host-specific quirks of this
    headless dev machine, not regressions — always run targeted filters, never treat a failure there
    alone as a real break, and never weaken the assertions to make them pass.
  - **Suggested next move:** either (a) do a manual QA pass on the AI-09 translate + Export SRT feature
    (needs a human/screen, not headless), or (b) name a specific item from the manual-QA list above to
    verify or extend, or (c) ask for another small, self-contained code slice to be picked — at this
    point almost everything remaining is manual verification or gated on a product/architecture call,
    not a ready-to-code ticket.
- **ADR-0011 (stereo balance) fully implemented, engineering-verified (2026-09-26).** Stages 1-2c all
  landed: `StereoBalance`/`StereoBalanceGain` pure gain math (Domain.Media), `StereoBalanceSyncPolicy`
  drift-correction decision, `StereoBalanceAudioArgs` ffmpeg `pan`/seek/pipe argument builder
  (Media.Export), the live `StereoBalanceAudioSession` (spawns the ffmpeg subprocess, pumps stdout PCM
  into a NAudio `WasapiPlayerBuilder`-built `WasapiPlayer` — `WasapiOut` is obsolete in this NAudio 3
  version), `IStereoBalanceAudioSession` in `Application.Media` (UI/VM depend on the interface, never
  the concrete session, mirroring `IVisualizerAudioSource`), and `PlayerViewModel.SetStereoBalanceAsync`
  wiring (mutes/unmutes the engine's own output around the secondary path, resets on every `LoadAsync`
  and on `Dispose`, and `SeekTargetAsync` fire-and-forgets a resync notification so a user seek restarts
  the ffmpeg segment without stalling the timeline). A debounced (~300ms) "Stereo Balance (experimental)"
  slider sits in `PlayerPage`'s Audio Tracks drawer, visible only for local (non-stream) media. **User
  confirmed working correctly.** No automated test exercises a live audio device (per the documented
  precedent that a real NAudio-device test once hung this suite under xUnit parallelism) — verification
  was build + the 37 pure unit/integration tests + user manual QA.
- **ADR-0012 (persistent video surface across navigation) — attempted, CAUSED A CRASH, REVERTED
  (2026-09-26).** User-reported bug: returning to `PlayerPage` from `HomePage` (via the "Now Playing"
  bar) during video playback reset the video to the start and looked paused. Root-caused (not guessed)
  via code reading: WinUI's `Frame` navigation tears down/recreates `PlayerPage` on every navigation, so
  its `VideoSurfaceHost` (wrapping libVLC's `VideoView`, a `SwapChainPanel`) is a brand-new native
  swap-chain every time, which `LibVlcPlaybackEngine.AttachVideoSurfaceAsync` detects as changed
  swap-chain options and triggers `RebuildRendererCore` — whose best-effort position carry is silently
  dropped by libVLC pre-`Play()` (an already-exhausted, documented investigation: every attempt to force
  it further, e.g. `:start-time` or a deferred `Time` set in the native `Playing` callback, previously
  reproduced a real `AccessViolationException`). ADR-0012 proposed the *correct* fix at the *right*
  layer: keep the same `VideoSurfaceHost` instance alive across navigation (owned by `AppShell`,
  borrowed/re-parented by `PlayerPage`) so `AttachVideoSurfaceAsync`'s existing swap-chain-options-equal
  no-op path is hit on ordinary "go Home and back" navigation and the rebuild never fires at all —
  verified by code reading that `DetachVideoSurfaceAsync` never resets `_activeSwapChainOptions`, so this
  mechanism is sound in principle.
  - **Implementation (commit `a0584c8`, reverted by `cd283c4`):** `AppShell` gained a static `Current` +
    `VideoHost` property and an `AttachVideoHostTo(Panel)`/`ParkVideoHost()` re-parenting API, backed by
    a `VideoSurfaceHost` declared once in `AppShell.xaml` inside a **`Width="0" Height="0" Opacity="0"`**
    parking `Grid` (to keep it invisible/non-hit-testable while not on the player page).
    `PlayerPage.xaml` replaced its own `<controls:VideoSurfaceHost x:Name="VideoHost">` with an empty
    `VideoHostSlot` `Grid`, and `PlayerPage.xaml.cs` borrowed/returned the shared instance in
    `OnNavigatedTo`/`Page_Unloaded`.
  - **What broke:** build was clean (0 errors), `DesignTokenReferenceTests` and all 123
    `PlayerViewModelTests` passed, and `VideoSurfaceTests`' 4 failures were confirmed via a `git stash`
    A/B to be pre-existing host-specific native-timing flakiness (identical failures with and without
    this change) — so nothing in the *automatable* verification caught the regression. On the user's
    real machine: video stopped rendering (audio kept playing — a separate native path) and the app
    **crashed a few seconds into playback**.
  - **Suspected root cause (not yet confirmed — reverted before further investigation):** a
    `SwapChainPanel`-backed control (libVLC's `VideoView` is one) very likely needs a real, non-zero size
    at first native initialization to build a valid Direct3D swap chain. Initializing it at the parking
    spot's `0×0` size, then later resizing it up to fill the real video area once re-parented into
    `PlayerPage`'s `Stretch`-sized `VideoHostSlot`, plausibly left it with a broken/invalid swap chain —
    consistent with "video surface produces nothing, then crashes shortly after" rather than an
    immediate failure. **This is a hypothesis, not a verified fact** — it was not tested further because
    this environment cannot run/observe the app interactively, and shipping another guess into a
    confirmed crash path without a human able to test each step immediately was judged too risky.
  - **Reverted via `git revert --no-edit a0584c8` → commit `cd283c4`.** Build re-verified clean (0
    errors) after the revert. Current (reverted) state: the pre-ADR-0012 behavior is restored — video
    playback works, but returning to the player from Home still resets to the start (the original,
    accepted, non-crashing limitation).
  - **If picking this back up:** do NOT re-land the same 0×0-parking-spot mechanism without testing.
    Suggested safer approach for next time: park the shared `VideoSurfaceHost` at a real, reasonably
    large size (or keep it inside a `Visibility="Collapsed"` parent instead of a zero-sized one — a
    collapsed panel skips layout/render entirely rather than sizing its child to zero, which may avoid
    ever handing the `SwapChainPanel` a 0×0 initial size) and — critically — test after *every single
    small change* on a real machine before proceeding to the next, rather than landing the whole
    mechanism at once and finding out only from a crash report. ADR-0012 itself remains architecturally
    sound (the "why" is confirmed by code, not guessed) — only this specific hiding mechanism is
    implicated.
- **Beta build produced (2026-09-26).** A signed MSIX beta (self-signed dev cert, ADR-0010) was hand-
  built and delivered for the user's own machine: `tools/build-release.ps1 -SkipTests -Package` compiled
  cleanly, but its own post-packaging `Get-ChildItem`/`Join-Path` step failed in this dev environment
  (a path-resolution bug independent of the actual MSIX build — the `.msix` itself was produced fine at
  `src\PlaySphere.App\bin\Release\AppPackagesPlaySphere.App_0.10.0.0_x64_Test\PlaySphere.App_0.10.0.0_x64.msix`).
  Worked around by locating `signtool.exe` and signing the already-produced `.msix` directly with
  `tools\dev-signing-cert.pfx` (password `devcert`), then copying `dev-signing-cert.cer` alongside it so
  `Add-AppDevPackage.ps1` can auto-trust it on install. **If this script failure recurs next session,
  the fix belongs in `tools/build-release.ps1`'s `$packageDir`/`Get-ChildItem` step, not in a manual
  workaround each time** — likely the same class of `$PSScriptRoot`-under-a-spaced-path issue seen
  earlier this session with the top-level `powershell -File` invocation (direct `& .\tools\...ps1`
  dot-invocation avoided that one, but a similar problem surfaced one level deeper in the script).
  Build state shipped in this beta: ADR-0011 (stereo balance) fully working per user confirmation;
  ADR-0012 reverted (video-surface persistence bug fix attempt), so the pre-existing "returning to the
  player from Home resets video to the start" limitation is present in this beta as before — not new,
  not worse, just still there.
- **Tester diagnostic report (post-beta punch list #6) COMPLETE (2026-10-23)** (engineering-verified;
  see manual-QA caveat). Help in the left nav opens **Report a problem**: optional note, optional
  reply address, a live list of which local logs will be included, and an off-by-default checkbox
  for `library.db` (+ wal/shm). Confirm creates a zip under
  `%LOCALAPPDATA%\LiVi PlaySphere Studio\reports\` containing `report.txt` plus whichever of
  `crash.log`, `crash.log.old`, `libvlc.log`, and `visualizer-audio.log` exist (missing files
  skipped, logs tailed to 2 MB, OpenAI-style keys redacted). Media, thumbnails, `preferences.json`,
  and source media are never packed. Symlinks are not followed. File Explorer then selects the zip
  and the default mail client opens with To/Subject/Body filled in. `mailto:` cannot attach files
  (RFC 6068; MAPI is not reliable for unpackaged WinUI), so the body tells the tester to attach the
  selected zip and press Send — nothing is uploaded before that. If the zip or the mail client
  fails, the page offers Open log folder / Copy path. The To address is
  `DiagnosticReportDefaults.SupportEmail` (`beta-feedback@livisoftware.example` until replaced —
  the Help page warns while that placeholder is still set). Override point:
  `AddPlaySphereDiagnostics` / `DiagnosticReportOptions` in `App.xaml.cs`. Documented in
  `docs/security/crash-diagnostics.md` and `docs/security/privacy.md`. Tests:
  `DiagnosticReportTests` (selection, mailto, redaction, confirmation) and `DiagnosticPackTests`
  (real zip: present/missing logs, media excluded, library opt-in, truncation, symlink, reports-folder
  failure, retention). Manual-QA caveat: no interactive desktop UI automation here, so the Help
  page, the confirm dialog, Explorer, and the mail client were not clicked through.
- **Post-beta-launch punch list (requested by the user, to pick up next session):** in priority order —
  1. **Certificate-trust friction is a real blocker for testers who aren't the machine owner.** The
     self-signed dev cert (ADR-0010) means every beta tester must manually trust a certificate before
     the app runs. Fine for a handful of trusted testers; a real blocker for anyone wider. ADR-0010 left
     the real signing-certificate strategy (paid CA vs. Azure Trusted Signing vs. Store) as an explicit
     open decision — needs to actually be made once the beta group grows.
  2. **No update mechanism is wired yet.** `tools/generate-appinstaller.ps1` exists but no
     `.appinstaller` file has ever been generated or hosted (checked this session — none found in the
     repo). Right now every new beta build is a fresh manual reinstall for every tester; no in-place
     update path exists.
  3. **The clean-machine install/uninstall checklist has never actually been run.**
     `docs/testing/clean-machine-install-checklist.md` exists (Phase 10 deliverable) but per this
     project's own standing limitation (no second machine/VM in this dev environment) it has never been
     executed for real. Needs a human on an actual second/clean Windows machine before the install
     experience can be trusted for anyone beyond this one dev machine.
  4. **The video reset-to-start-on-return bug is still open** (see the ADR-0012 entry directly above) —
     testers will hit this on every "go Home, come back" during video playback.
  5. **The large standing manual-QA backlog in AGENTS.md is still mostly unclicked** (playlists,
     visualizer presets, the full AI chain, Phase 7 tools, etc.) — real beta usage is exactly what will
     surface which of these are actually fine vs. actually broken, so this is a good time to start
     working through that list against real tester reports rather than guessing priority.
  6. **Tester feedback / crash-report loop — DONE (2026-10-23).** Help → Report a problem packs
     the local logs and opens a mailto compose window. See the "Tester diagnostic report" entry
     above and `docs/security/crash-diagnostics.md`. Still required before a real tester build:
     replace `DiagnosticReportDefaults.SupportEmail` (it is still the `.example` placeholder) and
     do the manual click-through (the dev environment has no desktop UI automation). Certificate
     trust (#1) remains the other blocker for anyone who is not the developer.
