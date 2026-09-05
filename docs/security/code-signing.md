# Code signing (Phase 10 / ADR-0010)

## What exists today: a self-signed dev certificate

`tools/create-dev-signing-cert.ps1` creates (or reuses) a self-signed certificate
(`CN=sarayunova`, matching `Package.appxmanifest`'s `Identity/Publisher`) in the current user's
certificate store and exports it to `tools/dev-signing-cert.pfx`.

**This certificate is not trusted by Windows, SmartScreen, or the Microsoft Store.** It exists
only so the MSIX package can be signed at all — MSIX requires every package to carry a valid
Authenticode signature whose subject matches the manifest's `Publisher`, even for purely local
testing. Packages signed with it will:

- Trigger a SmartScreen / "Windows protected your PC" warning on first run on a machine that
  hasn't explicitly trusted the certificate.
- **Fail to install entirely** on a machine where the certificate has not been imported into
  the Trusted People store, with an error to that effect from the installer.

The `.pfx` (and any `.cer` exported from it) must **never** be committed — `.gitignore` excludes
`*.pfx`/`*.cer`/`*.p12` repo-wide. Each developer generates their own local dev certificate by
running the script; it is not shared or checked in.

## Installing a dev-signed package on a test machine

1. Copy the produced `.msix` (and, if testing on a different machine than the one that signed
   it, the corresponding `.cer` — export one from the `.pfx` if needed) to the test machine.
2. Trust the certificate once, as an administrator:
   ```powershell
   Import-Certificate -FilePath <path-to-.cer> -CertStoreLocation Cert:\LocalMachine\TrustedPeople
   ```
   (The MSIX tooling's own generated `Add-AppDevPackage.ps1`, found alongside the `.msix` in
   `AppPackages/`, automates exactly this step plus the install — use it directly for convenience.)
3. Double-click the `.msix` (or run `Add-AppSideloadDependencyPackage`/`Add-AppxPackage`) to
   install.

## What this is *not*

A self-signed certificate is not a real trust chain. It does not make this build safe to
distribute publicly, does not satisfy Microsoft Store submission requirements, and provides no
protection against tampering claims beyond "this specific file wasn't modified after this
specific local key signed it." **The real signing strategy for actual distribution — a
commercial CA certificate, Azure Trusted Signing, or Microsoft Store signing — is a deliberate
open decision, not yet made** (see ADR-0010's "Consequences" section). Do not treat the
self-signed cert as a placeholder that "will just get replaced" without someone explicitly
choosing and setting up one of those options first.

## Signing a package

`tools/build-release.ps1` has an optional signing step that locates the local `.pfx` (see the
script for exact parameters) and signs the packaged `.msix` via `signtool sign`. It is skippable
with `-SkipSign`, which CI uses by default since no certificate exists in that environment.
