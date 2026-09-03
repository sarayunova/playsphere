---
name: accessibility-audit
description: Keyboard navigation, contrast, high DPI, reduced motion and screen-reader checks for PlaySphere.
---

# Accessibility Audit

## Requirements (§45)
Keyboard navigation, visible focus, accessible names, high-contrast compatibility, color-independent states, subtitle scaling, reduced motion, and screen-reader-compatible core transport controls.

## UI acceptance (§59)
Every screen: hierarchy, contrast, keyboard, dark/light, high DPI, resizing, loading/empty/error/disabled/hover/focus states.

## Implemented highlights
- Visualizer reduced-motion drops to ~30 FPS (`ReducedMotion` from `UISettings.AnimationsEnabled`).
- Presets are non-flashing by design.
- `[`/`]` (Mark In/Out), Space/arrows/M/R/S/Esc, `.`/`,` (next/prev), F/F11/Alt+Enter fullscreen, P mini-player shortcuts.
- High-contrast and color-independent states are design tokens; not yet fully audited live.

## Audit tooling
Windows UI Automation tests are a P2 item (`tests/PlaySphere.UITests` is still an empty stub). A real interactive-desktop pass (no automation in this dev environment) is the standing manual-QA for visual features.
