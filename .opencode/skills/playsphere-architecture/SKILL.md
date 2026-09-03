---
name: playsphere-architecture
description: Repository boundaries, dependency rules and architecture enforcement for the PlaySphere modular monolith.
---

# PlaySphere Architecture

## Rules
- Modules depend inward: UI → Application → Domain. Each module owns its features.
- `src/PlaySphere.App` is the only composition root; it may reference any module.
- UI must never reference libVLC, FFmpeg or OpenAI types directly.
- Changing playback engine, DB, UI framework, AI provider, packaging, GPU tech or repo boundaries requires an ADR (PICKLE.md §58).
- Add a new project only when its first real feature lands; keep the solution lean.
- See `docs/architecture/modular-monolith.md`.