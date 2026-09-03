---
name: release-engineering
description: Release pipeline, versioning, signing, SBOM and distribution for PlaySphere Studio.
---

# Release Engineering

## Target (PICKLE.md §4 / §52 Phase 10)
MSIX packaging, code signing, clean-machine install testing, update mechanism, crash diagnostics, dependency license audit, SBOM, and privacy documentation.

## Current state
- App is an **unpackaged** WinExe (ADR-0001). Phase 10 release engineering is NOT started.
- `tools/build-release.ps1` runs restore → build → test → smoke in sequence.
- `tools/verify-environment.ps1` checks dotnet + NuGet.
- App icon embedded + `AppWindow.SetIcon` at ctor.

## Phase 10 checklist
1. MSIX packaging (+ optional signed EXE/MSI bootstrapper).
2. Code signing (trusted cert).
3. Clean-machine install/uninstall test on a VM (§53).
4. Update mechanism.
5. Crash diagnostics (dump collection) — see the crash-reporting skill.
6. Dependency license audit + SBOM.
7. Privacy documentation.
8. Do NOT bundle the ~98MB ffprobe wholesale — packaging decision needed.

## ADR trigger
Packaging mechanism changes require an ADR (§58).
