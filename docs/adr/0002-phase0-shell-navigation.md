# ADR-0002 — Phase 0 shell: WinUI NavigationView + route registry

Status: **Accepted** (Phase 0).
Date: 2026-08-30.

## Decision

- Left navigation uses `NavigationView` inside `AppShell` (a `Page` hosted by `MainWindow`).
- Menu items are generated from a single C# source of truth (`ShellMenu`) rather than being
  duplicated in XAML, so navigation route tests can verify menu ↔ route ↔ page consistency.
- Design tokens (colors, brushes, gradients, spacing, corner radii) live in
  `PlaySphere.UI/Themes/DesignTokens.xaml` and are merged at the app root.

## Consequences

- Navigation is unit-testable without a UI thread (route registry + menu contract).
- No libVLC, no AI, no data access in Phase 0 — per PICKLE.md §62.
- Later phases may move to shell regions/panes; the route registry remains the contract.

## Alternatives considered

- XAML-declared `NavigationViewItem`s: rejected (duplicated list, untestable).
- Third-party shell: rejected (full control and no extra dependency needed).