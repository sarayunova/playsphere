---
name: advanced-gpu-shaders
description: Win2D / Direct3D effect layer and custom shader work for the PlaySphere visualizer.
---

# Advanced GPU Shaders

## Current baseline
- Rendering is Win2D (`CanvasAnimatedControl`) via `IRenderPreset` in `PlaySphere.UI.Visualizer`. Presets use only session color-taking primitives (`DrawLine`/`DrawCircle`/`FillCircle`) with no per-frame allocation.
- Direct3D extension point is reserved in the visualizer architecture (§33: "Optional Direct3D effect layer") but not yet used.

## Principles
- Any custom shader must remain allocation-free per frame and drop visualizer frames before audio (§33/§35).
- Presets must be non-flashing by design (no strobe) to satisfy "Disable Flashing" accessibility.
- GPU tech changes require an ADR (§58).

## Status
Not implemented. Five background presets + four accent presets all use CPU primitives. Add effects only when visual polish justifies the GPU complexity.
