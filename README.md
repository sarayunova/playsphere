# LiVi PlaySphere Studio

**Play Everything. See More. Understand Your Media.**

LiVi PlaySphere Studio is a universal Windows media player and intelligent media workspace
developed by **LiVi Software Company**. Broad local media playback, a fast and polished
Windows-native experience, vibrant modern visual design, advanced subtitles and audio controls,
media library and playlists, timeline/chapters/bookmarks, classic-style music visualizers,
conversion and clip export, and optional AI understanding — all offline-first by default.

## Repository layout

```
src/
  PlaySphere.App/              WinUI 3 launcher app (composition root)
  PlaySphere.UI/               Shell, pages, navigation, design tokens
  PlaySphere.Domain/           Core domain model
  PlaySphere.Application/      Application services and use cases
  PlaySphere.Playback.Abstractions/
  PlaySphere.Infrastructure/
tests/
  PlaySphere.UnitTests/
samples/  tools/  docs/
```

## Build and test

```powershell
dotnet restore
dotnet build -c Release
dotnet test -c Release
```

Launch smoke test:

```powershell
powershell -ExecutionPolicy Bypass -File tools/run-app-smoke.ps1
```

## Status

See `docs/` and `PICKLE.md` for the authoritative build blueprint. Implementation currently
tracks **Phase 0 — Repository Foundation** (PS-001..PS-005).