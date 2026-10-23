---
name: crash-reporting
description: Native and managed crash capture, triage and diagnostics for PlaySphere.
---

# Crash Reporting

## Managed crashes
`.NET Runtime` events and `crash.log` capture managed exceptions. `AccessViolationException` is corrupted-state — ordinary `try/catch` in .NET Core cannot catch it, ever.

## Native crashes (Win2D / libVLC)
Native AVs bypass the managed handler entirely. Key lessons recorded in AGENTS.md:
- WER `LocalDumps` was configured but never reliably produced a `.mdmp` for native AVs — don't rely on it.
- What worked: launching the Release exe directly under WinDbg and letting it break on the first-chance AV; or ProcDump (download directly from `download.sysinternals.com/files/Procdump.zip`).
- Known native-AV risk classes: rebuilding the libVLC player mid-playback (race with the live audio thread → heap corruption), per-frame Win2D gradient-brush allocation, `CanvasAnimatedControl.RemoveFromVisualTree()` on a not-yet-in-tree canvas, swap-chain resize racing an in-flight `Draw`.

## Boundaries
Never attempt risky native object rebuilds to hack around a crash class (see the reverted visualizer Stop→Play cycle). Prefer safe managed reload/rebind paths and deferrals.

## Tester reports
Help → Report a problem packs local logs into a zip and opens a mailto compose window. Nothing is uploaded. See `docs/security/crash-diagnostics.md`. Change `DiagnosticReportDefaults.SupportEmail` before a tester build. Do not add Sentry, App Center, or any auto-upload.
