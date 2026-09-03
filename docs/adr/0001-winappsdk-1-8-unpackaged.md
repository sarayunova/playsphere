# ADR-0001 — Windows App SDK 1.8 + unpackaged self-contained for Phase 0

Status: **Accepted** (Phase 0).
Date: 2026-08-30.

## Decision

- Use **Microsoft.WindowsAppSDK 1.8.260804001** (latest stable 1.8 servicing) rather than the
  2.x line. WinAppSDK 2.x splits WinUI into `Microsoft.WindowsAppSDK.WinUI` with a different
  release cadence; 1.8 is a proven, stable servicing line and satisfies Phase 0 needs.
- The launcher builds **unpackaged** (`WindowsPackageType=None`) with the Windows App SDK
  runtime **self-contained** in the app output (`WindowsAppSDKSelfContained=true`) so it runs
  on clean dev machines and CI without installing the runtime.
- **MSIX packaging, code signing and installer engineering are deferred to Phase 10**
  (Release Engineering) which is where packaging is scheduled in PICKLE.md.

## Consequences

- Phase 0-9 development and tests run against the unpackaged build; no cert required.
- Phase 10 adds the MSIX packaging project + signing; no rearchitecture needed because
  WinAppSDK supports both modes from the same codebase.
- Re-evaluate WinAppSDK 2.x migration under a new ADR when WinUI 3 components settle.

## Alternatives considered

- WinAppSDK 2.4.0: rejected for Phase 0 (structural changes, separate WinUI package).
- Packaged MSIX now: rejected (certificate/signing friction ahead of Release Engineering).