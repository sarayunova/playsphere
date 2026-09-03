# Building a Modular Monolith

PlaySphere is a **modular monolith**:

- Single deployable (MSIX first, optional bootstrapper later).
- Clear compile-time module boundaries via separate projects under `src/`.
- Dependencies flow inward toward `PlaySphere.Domain`.
- UI layers must never call libVLC, FFmpeg or OpenAI directly; they go through
  abstraction/service projects.

## Dependency rule

```
App → UI → Application → Domain
App → Application, Infrastructure, Playback.Abstractions
Playback.Abstractions has no runtime deps
Media/Visualizer/AI modules depend on Domain + Application only
```

Any change to these boundaries requires an ADR (see `docs/adr/`, PICKLE.md §58).