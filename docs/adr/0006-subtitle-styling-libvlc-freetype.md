# ADR-0006 — Subtitle font/color/outline/position styling via libVLC per-media freetype options

Status: **Accepted** (Phase 3 continuation).
Date: 2026-08-31.

## Context

Prior to this ADR, `SubtitleStyleOptions` (PICKLE.md §17 "styling") exposed only
`TextScalePercent` (25–500%), applied through libVLC's `sub-text-scale` media option. Font,
text/outline color, outline thickness and vertical position were deferred because they were
freetype-module-level libVLC settings whose per-media applicability and on-screen rendering
could not be verified headlessly in this environment.

For a commercial player the ability to style plain-text subtitles (SRT/WebVTT) is expected.
The alternatives were:

1. **libVLC per-media freetype options** — extend the existing, already-working per-media
   option mechanism (the same one that carries `:sub-text-scale=`) with the freetype renderer's
   own options (`freetype-font`, `freetype-color`, `freetype-outline-color`,
   `freetype-outline-thickness`) plus the core `sub-margin` option for vertical position.
2. **Do the styling ourselves (Win2D text layout)** — discard libVLC's renderer and draw
   subtitle text ourselves. This is a large, cross-cutting change to the video overlay path.

## Decision

Use **option 1**: keep the mandated libVLC engine and its freetype text renderer, and extend
`SubtitleStyleOptions` with font, text color, outline color, outline thickness and vertical
margin/position, applied per-media as media options — the identical mechanism to the existing
`:sub-text-scale=`.

- `SubtitleStyleOptions` (Domain) is extended to a multi-field record: text scale (existing),
  `FontFamily`, `TextColorArgb`/`OutlineColorArgb` (0xRRGGBB), `Outline`
  (`SubtitleOutlineStyle` Off/Thin/Normal → freetype thickness 0/1/2) and `VerticalMarginPx`
  (→ `sub-margin`). `TextScalePercent` stays first so existing constructors still compile.
- The engine clamps out-of-range values (`PlaybackConstraints.ClampSubtitleColor` →
  0–0xFFFFFF, `ClampVerticalMarginPx` → 0–1000px, text scale 25–500) before composing options,
  and only non-default fields are emitted.
- The UI still never touches libVLC: `PlayerViewModel` exposes styled setters that build the
  next `SubtitleStyle` and call `IMediaPlaybackEngine.SetSubtitleStyleAsync`.
- Styling applies to **plain-text** subtitles (SRT/WebVTT). ASS/DVD subtitles render their own
  baked-in style and are unaffected (consistent with video track containing burned styling).
- The freetype option names were confirmed present in the deployed win-x64
  `libfreetype_plugin.dll` and `libvlccore.dll` before any code was wired.

## Consequences

- Extends PICKLE.md §17's `SubtitleStyleOptions` without changing the engine, DB, UI framework
  or repo boundaries (no new project). Same per-media option mechanism as `sub-text-scale`,
  so no layout/packaging change.
- Settings only take effect on the media the options are attached to — i.e. user can change
  styling mid-playback, but it applies from the next `LoadAsync` (not hot-swapped on the current
  renderer), identical to the existing text-scale behaviour.
- A fixed latent bug: the old text-scale setter rebuilt a fresh default `SubtitleStyle` that
  would have wiped user-set font/color; each setter now mutates only its field via
  `engine.SubtitleStyle with { ... }`.

## Alternatives considered

- Win2D custom subtitle rendering (rejected: large cross-cutting change to the overlay path,
  discards libVLC's renderer, and duplicates features libVLC already provides).
- Per-module libVLC global settings (`libvlc_set_*` freetype module vars) instead of per-media
  options (rejected: global config would affect every subtitle stream and can't be scoped per
  file).
- Doing nothing / keeping text-scale only (rejected for a commercial player where subtitle
  styling is an expected feature).

## Verification

Engineering-verified: `dotnet build -c Release` clean (0 warnings/errors), full suite green
(159 unit + 31 integration + 58 playback, 1 skip), and `tools\run-app-smoke.ps1` launches the
app. The actual on-screen rendering of font/color/outline/margin was **not** visually verified
headlessly — only option emission is engineering-verified. A manual click-through (open an SRT
video, change each style control, reload, confirm it renders and persists) is required before
calling this closed end-to-end.
