# Crash diagnostics (Phase 10)

## What exists

`App.xaml.cs` writes unhandled exceptions to a local, append-only log:
`%LOCALAPPDATA%\LiVi PlaySphere Studio\crash.log`. Nothing here is transmitted anywhere - this is
local-only diagnostic data, consistent with the app having no telemetry (see
[privacy.md](privacy.md)). There is deliberately no remote crash-reporting service (Sentry,
App Center, Application Insights, etc.) - adding one would mean picking a vendor and deciding
what leaves the machine, which is a product decision for the project owner, not something to
default into silently.

Three places are hooked, covering every way .NET surfaces an unhandled exception:

- `Application.UnhandledException` (WinUI/XAML) - exceptions on the UI thread. This was the only
  one covered before Phase 10.
- `AppDomain.CurrentDomain.UnhandledException` - exceptions on any other thread. Before Phase 10
  this was **not** hooked, meaning an unhandled exception on a raw background `Thread` crashed the
  process with zero log entry - the single most important gap this phase closes.
- `TaskScheduler.UnobservedTaskException` - exceptions from `Task`s whose exception was never
  observed (e.g. a fire-and-forget task that threw). These don't crash the process on modern .NET,
  but logging them surfaces real bugs that would otherwise be silently swallowed. The event is
  marked observed (`e.SetObserved()`) after logging so it can't still trigger any legacy
  finalizer-thread behavior.

Each entry records a timestamp, which of the three sources it came from, the app version
(`Windows.ApplicationModel.Package.Current.Id.Version` when running packaged, falling back to the
assembly version for unpackaged dev runs), the OS version, and the full exception (including inner
exceptions and stack trace via `Exception.ToString()`).

## Log rotation

`crash.log` is capped at 5 MB: if it would exceed that on the next write, the current file is
copied to `crash.log.old` (overwriting any previous rotation) and truncated. This exists so a
crash-loop can't grow the file unboundedly - it does not guarantee against store-full/permission
edge cases, which are caught and swallowed (logging failures must never take the app down further
than the original crash already did).

## What this doesn't do

- No automatic reporting, upload, or user prompt to send `crash.log` anywhere - a user or
  developer must find and share it manually if diagnosing a real user's crash.
- No structured crash grouping/deduplication (that's what a real crash-reporting service would
  add) - this is a flat text log meant for manual inspection, not a dashboard.
- No dump-file (`.dmp`) capture - only the managed exception's message and stack trace, which
  covers most .NET-level bugs but not a native-code crash inside libVLC/ffmpeg itself (those
  still terminate the process without a managed exception to catch; the existing `libvlc.log`
  native message log is the closest existing diagnostic for that class of failure).
