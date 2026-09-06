# Clean-machine MSIX install/uninstall checklist (Phase 10 / ADR-0010)

This verifies the packaging claims in ADR-0010 on a machine that has never run PlaySphere and
does not already trust the dev signing certificate. It **must be run by a human on a real machine
or VM** — it cannot be automated from the development environment, since that environment already
has the dev certificate trusted and the SDK/source installed, so it can never be "clean."

Required whenever packaging changes, per PICKLE.md §53.

## Prerequisites

- A Windows 10 (build ≥ 10.0.17763) or Windows 11 machine/VM that has never had PlaySphere
  installed and does not have the `CN=sarayunova` dev certificate in its Trusted People store.
- The signed `.msix` from `src/PlaySphere.App/bin/Release/AppPackages/<version>/`, produced by
  `tools/build-release.ps1 -Package` (not `-SkipSign` — an unsigned package won't install).
- The corresponding `Add-AppDevPackage.ps1` and `.cer`, generated alongside the `.msix` in the
  same `AppPackages/<version>/` folder.
- Copy the whole `AppPackages/<version>/` folder to the test machine (not just the `.msix`) —
  `Add-AppDevPackage.ps1` expects the `.cer` and dependency packages next to it.

## Install

1. On the clean machine, open PowerShell as Administrator in the copied folder and run
   `.\Add-AppDevPackage.ps1`. Confirm it reports trusting the certificate and installing the
   package without errors.
2. Confirm "LiVi PlaySphere Studio" appears in the Start menu with the correct tile/icon (the
   placeholder icon set from `tools/generate-msix-assets.ps1` — icons will look plain/generic
   until real artwork replaces them; that's expected for this phase, not a failure).
3. Launch it from the Start menu (not from a shell in the install folder) — this is the actual
   packaged-app launch path being verified, not just that the exe runs.

## Functional smoke test

4. Open a local media file and confirm playback starts (video + audio).
5. Confirm the app can create its data folder: check that
   `%LOCALAPPDATA%\LiVi PlaySphere Studio\` exists and contains `preferences.json` after changing
   any setting. This is the ADR-0010 claim under test — that `runFullTrust` MSIX packaging needs
   no code change to `Environment.SpecialFolder.LocalApplicationData` access.
6. Exercise one feature that writes to that folder beyond preferences (e.g. generate a thumbnail
   strip or waveform by scrubbing/loading a file) and confirm the corresponding subfolder
   (`thumbstrips`, `waveforms`, or `thumbs`) is created there, not elsewhere.
7. Confirm no SmartScreen/"Windows protected your PC" block prevented launch (a warning dialog
   before you approved it is fine and expected for a dev-signed package; an outright block is not
   — the `Add-AppDevPackage.ps1` trust step should have prevented that).

## Uninstall

8. Uninstall via Settings → Apps → "LiVi PlaySphere Studio" → Uninstall (or
   `Get-AppxPackage *PlaySphereStudio* | Remove-AppxPackage`).
9. Confirm the Start menu entry is gone and the app's installed files under
   `%ProgramFiles%\WindowsApps\` (or wherever `Get-AppxPackage` reported `InstallLocation`) are
   removed.
10. Decide/record whether `%LOCALAPPDATA%\LiVi PlaySphere Studio\` should survive uninstall
    (normal Windows convention for per-user data) — note whether it did, since this isn't
    currently asserted one way or the other by the packaging.

## Record the result

Note the date, OS build, and pass/fail (with details on any failure) here or in the PR that next
touches packaging, so there's a record this manual gate was actually run rather than assumed.
