---
name: windows-packaging
description: MSIX, code signing, clean-machine install, auto-update and installer testing for PlaySphere.
---

# Windows Packaging

## Strategy (PICKLE.md §4)
MSIX first; optional signed EXE/MSI bootstrapper. Currently the app is an **unpackaged** WinExe per ADR-0001 (that's why the DB/icon/logs resolve via `Environment.SpecialFolder.LocalApplicationData`, not `Windows.Storage.ApplicationData`).

## Icon
- Embedded via `<ApplicationIcon>Assets\app.ico</ApplicationIcon>` in `src\PlaySphere.App\PlaySphere.App.csproj` (copied to output).
- WinUI titlebar/taskbar icon needs `AppWindow.SetIcon(...)` at ctor time (embedded exe icon alone doesn't surface there).

## Phase 10 (release engineering) — not started
MSIX packaging, code signing, clean-machine install testing, update mechanism, crash diagnostics, dependency license audit, SBOM, privacy documentation.

## Packaging constraints
- Don't bundle the ~98MB `ffprobe.exe` wholesale; deployed scans rely on the tool next to the app (fallback locator) or degrade gracefully. Bundling is a later packaging decision.
- Any packaging mechanism change is an ADR trigger (§58).

## Release verification (§53)
If packaging changed: run a clean VM install/uninstall test.
