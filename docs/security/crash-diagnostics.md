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

## Sending a report from the app

Help → **Report a problem** (left navigation) packs the local diagnostic files that exist and
opens the tester's own email app. Nothing is uploaded. The tester reviews the message and presses
Send themselves. There is still no Sentry, App Center, Application Insights, or any other
automatic crash service.

The zip is written under `%LOCALAPPDATA%\LiVi PlaySphere Studio\reports\`
(`PlaySphere-diagnostics-yyyyMMdd-HHmmssfff.zip`). It contains `report.txt` (the optional note,
optional reply address, app version, and a list of what was included) plus whichever of these
files are actually on disk:

| File | What it is |
|---|---|
| `crash.log` | Unhandled managed exceptions and playback errors |
| `crash.log.old` | The previous `crash.log`, after the 5 MB rotation above |
| `libvlc.log` | libVLC native diagnostics |
| `visualizer-audio.log` | Visualizer WASAPI loopback trace |

Missing files are skipped. Each text log is tailed to the last 2 MB so a huge `libvlc.log` cannot
turn the zip into an unbounded attachment. OpenAI-style API keys and bearer tokens in those logs
(and in the typed note) are redacted before they are packed.

**Not included**, even if they sit in the same folder: media files, thumbnails, `preferences.json`,
and source media. `library.db` (and its `library.db-wal` / `library.db-shm` sidecars) is included
only when the tester checks **Include library database**. That box is off by default. A database
larger than 16 MB is skipped rather than truncated, because a partial SQLite file is not useful.
Symlinks are not followed.

### Why the email does not attach the zip by itself

`mailto:` (RFC 6068) has no attachment parameter. A non-standard `attach=` query works only in
some desktop Outlook versions and fails silently everywhere else, so it is not used. MAPI
(`MAPISendMail`) can attach a file, but only when a desktop MAPI client is installed — the
Windows 11 Mail app and new Outlook do not reliably expose that to an unpackaged WinUI process.
The app therefore:

1. Saves the zip in the `reports` folder above.
2. Opens File Explorer with that zip selected (`explorer.exe /select`).
3. Opens the default mail client with To, Subject, and Body filled in. The body names the zip
   path and asks the tester to drag it in or use Attach, then press Send.

If the zip cannot be created, or no mail client can be opened, the page shows the reason and
offers **Open log folder** and **Copy path** so the tester is not stuck.

Zips in `reports` older than 14 days are deleted the next time a report is created. The logs
themselves are not deleted.

### Support address

The To address is `DiagnosticReportDefaults.SupportEmail` in
`src/PlaySphere.Application/Diagnostics/DiagnosticReportDefaults.cs`
(overridable at startup via `DiagnosticReportOptions` passed to `AddPlaySphereDiagnostics`).
The checked-in value is the placeholder `beta-feedback@livisoftware.example` — a reserved
`.example` domain, so an unconfigured build cannot deliver mail. **Change that constant to the
real support mailbox before shipping a tester build.** The Help page shows a warning while the
placeholder is still in place. Testers cannot retarget the address from the UI.

## What this doesn't do

- No automatic reporting or upload. The email is sent only if the tester presses Send.
- No structured crash grouping/deduplication (that's what a real crash-reporting service would
  add) - this is a flat text log meant for manual inspection, not a dashboard.
- No dump-file (`.dmp`) capture - only the managed exception's message and stack trace, which
  covers most .NET-level bugs but not a native-code crash inside libVLC/ffmpeg itself (those
  still terminate the process without a managed exception to catch; the existing `libvlc.log`
  native message log is the closest existing diagnostic for that class of failure, and it is
  included in the zip when the file exists).
