---
name: localization
description: String externalization and localized resource handling for PlaySphere.
---

# Localization

## Principles
- Externalize user-facing strings to resource dictionaries / `.resw` (WinUI), never hard-code them in XAML or code-behind.
- Keep the vibrant UI free of hard-coded labels; the navigation, drawers, dialogs and transport tooltips (which currently carry literal text) are the externalization targets.
- Consider RTL layouts and text expansion for non-Latin locales.
- Locale-aware number/date formatting via `CultureInfo.InvariantCulture` for parsing (untrusted input) vs. current culture for display.

## Status
Not implemented. This is a P2 skill; add the first localized strings when the first real locale work begins.
