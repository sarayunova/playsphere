---
name: modern-windows-ui
description: Vibrant design tokens, adaptive layout, Fluent-inspired behavior, animation and accessibility for PlaySphere.
---

# Modern Windows UI

## Rules
- Default theme is Vibrant Dark (PICKLE.md §7). Use the shared token dictionaries in `Themes\DesignTokens.xaml` (`Brush*`/`Color*`/`Gradient*`/`Spacing*`/`Control*`/`Card*`/corner radii) — never hard-code colors.
- Any new `{ThemeResource ...}` referencing a custom token MUST exist in `DesignTokens.xaml`, or `DesignTokenReferenceTests` fails (and the app crashes at runtime navigation, not compile time).
- Cards: rounded 8–12px corners, hover lift, restrained blur/glass only where readable. Poster cards built via `MediaCardBuilder.CreatePoster`.
- Use `XamlApplication = Microsoft.UI.Xaml.Application` alias in `PlaySphere.*` namespace files when referencing `Application` — the `PlaySphere.Application` namespace shadows the bare name.
- Primary accent gradient: `#36A9FF → #8B5CF6 → #EC4899`. Use only for accent/glow/selection/progress/hero, never behind long-form text.
- Follow the UI acceptance checklist (§59): hierarchy, spacing, keyboard, dark/light contrast, high DPI, resizing, loading/empty/error/disabled/hover/focus states.
- Drawers/sheets over permanent chrome (§9/§10). No permanent technical inspector.
- Unpackaged WinUI app → use HWND-initialized FilePicker/FolderPicker patterns (`FilePickerHelper`).
