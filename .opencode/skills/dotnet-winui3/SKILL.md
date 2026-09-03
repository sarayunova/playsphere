---
name: dotnet-winui3
description: C#, .NET 10, WinUI 3, Windows App SDK, XAML, MVVM, DPI and packaging conventions for PlaySphere.
---

# .NET / WinUI 3

## Stack
- Language C# 14, runtime .NET 10 LTS, UI WinUI 3 via Microsoft.WindowsAppSDK 1.8
  (see `docs/adr/0001`). MSIX packaging deferred to Phase 10.
- Target framework: `net10.0-windows10.0.19041.0`. Unpackaged dev build (`WindowsPackageType=None`,
  `WindowsAppSDKSelfContained=true`).

## Conventions
- MVVM: views in XAML, logic in ViewModels, no code-behind beyond wiring.
- Design tokens live in `src/PlaySphere.UI/Themes`. Never hard-code hex colors in pages.
- Respect Windows high-DPI; use `PerMonitorV2` (app.manifest) and adaptive layouts.
- Verify at 1366x768, 1920x1080, 2560x1440 and 4K high DPI before claiming UI complete.
- New pages: add route + page type in `Navigation/RouteRegistry`, menu item in `Navigation/ShellMenu`,
  and update navigation tests.