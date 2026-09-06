# Update mechanism: MSIX App Installer via GitHub Releases (Phase 10 / ADR-0010)

## What this is

ADR-0010 chose MSIX App Installer (`.appinstaller`), pointed at this repository's GitHub
Releases, as the update mechanism - not the Microsoft Store, since no Store developer account is
confirmed to exist for this project. App Installer needs only a stable download URL, which GitHub
Releases already provides.

`.github/workflows/release.yml` builds this automatically: pushing a tag matching `vX.Y.Z`
packages, signs, and publishes a GitHub Release carrying three assets:

- `PlaySphere.App_X.Y.Z.0_x64.msix` - the package itself.
- `PlaySphere.App.cer` - the public half of the certificate that signed it.
- `PlaySphere.App.appinstaller` - the file that lets Windows install and later auto-update the
  app from this release's URL.

## Cutting a release

1. Bump the version in **both** `src/PlaySphere.App/PlaySphere.App.csproj`'s `<Version>` and
   `src/PlaySphere.App/Package.appxmanifest`'s `Identity/Version` (the csproj comment on `Version`
   says why these aren't a single source of truth - single-project MSIX tooling doesn't wire one
   from the other). They must agree on the first three parts (csproj is 3-part, e.g. `0.11.0`;
   manifest is 4-part, e.g. `0.11.0.0`).
2. Commit that version bump.
3. Tag the commit `vX.Y.Z` matching the csproj `Version` exactly, and push the tag:
   `git tag v0.11.0 && git push origin v0.11.0`.
4. `release.yml` runs on the tag push. Its first step verifies the tag matches the csproj
   `Version` and fails fast with a clear error if you forgot step 1 - it will not silently publish
   a mismatched release.

## Installing / updating as a user

**First install:** download `PlaySphere.App.cer` and `PlaySphere.App.appinstaller` from the
release page. Trust the certificate once (as Administrator):

```powershell
Import-Certificate -FilePath PlaySphere.App.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
```

Then double-click `PlaySphere.App.appinstaller` to install. Windows will check this same URL for
updates automatically (once every 24 hours by default, and also on each app launch).

**Skipping certificate trust results in Windows refusing the install** with
`0x800B010A - the certificate could not be verified` - this is expected for a self-signed
certificate; see `docs/security/code-signing.md`.

## The re-trust-every-release limitation

`release.yml` generates a **new throwaway self-signed certificate on every run** (via
`tools/create-dev-signing-cert.ps1`, which creates one if none exists on that ephemeral runner -
and a fresh GitHub Actions runner never has one). This means:

- Every release is signed by a *different* certificate, even though all certificates share the
  same Subject (`CN=sarayunova`) required to match `Package.appxmanifest`.
- A machine that trusted a previous release's certificate must **trust the new one again** before
  App Installer's automatic update will succeed - the old trust doesn't carry over.
- This is a known, accepted limitation of this phase, not a bug. ADR-0010 explicitly defers the
  real signing-certificate decision (a paid CA certificate, Azure Trusted Signing, or Microsoft
  Store signing); once one of those is chosen, this limitation goes away because the certificate
  (and its trust chain) stops changing between releases.

## What isn't wired up yet

- No cadence or branch-protection rule enforces when tags get cut - this is a manual, deliberate
  action by whoever is doing the release.
- The GitHub Release's auto-generated notes (`--generate-notes`) are just the commit list between
  tags; nothing curates them into user-facing release notes yet.
