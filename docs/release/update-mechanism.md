# Update mechanism: MSIX App Installer via public GitHub Releases (Phase 10 / ADR-0010)

## What this is

ADR-0010 chose MSIX App Installer (`.appinstaller`), pointed at this repository's GitHub
Releases, as the update mechanism — not the Microsoft Store, since no Store developer account is
confirmed to exist for this project. Windows App Installer downloads the package itself and later
polls the same App Installer file for a newer `Version`. It does not send GitHub credentials, so
this works only while `sarayunova/playsphere` is a **public** repository.

`.github/workflows/release.yml` builds this automatically: pushing a tag matching `vX.Y.Z`
packages, signs, and publishes a non-prerelease GitHub Release. That release becomes GitHub's
`latest` release. It carries three assets:

- `PlaySphere.App_<version>_x64.msix` — the package itself, under the file name the MSIX
  packager produced (for example `PlaySphere.App_0.10.0.0_x64.msix`).
- `PlaySphere.App.cer` — the public half of the certificate that signed it.
- `PlaySphere.App.appinstaller` — the file Windows installs from and later polls for updates.

## Stable update URIs

`tools/generate-appinstaller.ps1` writes these URLs into the published `.appinstaller`. Both use
GitHub's public latest-download pattern and the exact asset file name uploaded for that release:

- App Installer file (the URL Windows stores and polls):
  `https://github.com/sarayunova/playsphere/releases/latest/download/PlaySphere.App.appinstaller`
- Package (the `MainPackage` URL inside that file):
  `https://github.com/sarayunova/playsphere/releases/latest/download/PlaySphere.App_<version>_x64.msix`
- Certificate (not inside the `.appinstaller`; users trust it once per release while signing stays
  self-signed):
  `https://github.com/sarayunova/playsphere/releases/latest/download/PlaySphere.App.cer`

`/releases/latest/download/<asset-name>` resolves to that exact file name on the release GitHub
currently marks latest. The workflow uploads `PlaySphere.App.appinstaller` and
`PlaySphere.App.cer` under those stable names on every release, and uploads the `.msix` under
the packager's file name. The generator is given that `.msix` name (`-MsixFileName`) and refuses
anything that is not a single file name, so the URL cannot drift from the uploaded asset.

On an update check Windows downloads the App Installer file from its stable URL, then reads the
package URL from **that** file. Each release's `.appinstaller` names the `.msix` that same
release uploaded, so the package URL resolves for as long as that release is `latest`. The next
tag publishes a new `.appinstaller` at the same stable URL, with the new package version and the
new `.msix` file name. A tag-pinned URL such as
`https://github.com/sarayunova/playsphere/releases/download/vX.Y.Z/PlaySphere.App.appinstaller`
would keep returning that tag's file after a newer release exists, and installed clients would
not see the update.

Publish tags in ascending version order, as a normal (non-prerelease) release. GitHub's `latest`
pointer follows the most recently published non-prerelease, which is what these URLs track.

## Cutting a release

1. Bump the version in **both** `src/PlaySphere.App/PlaySphere.App.csproj`'s `<Version>` and
   `src/PlaySphere.App/Package.appxmanifest`'s `Identity/Version` (the csproj comment on `Version`
   says why these aren't a single source of truth — single-project MSIX tooling doesn't wire one
   from the other). They must agree on the first three parts (csproj is 3-part, e.g. `0.11.0`;
   manifest is 4-part, e.g. `0.11.0.0`).
2. Commit that version bump.
3. Tag the commit `vX.Y.Z` matching the csproj `Version` exactly, and push the tag:
   `git tag v0.11.0 && git push origin v0.11.0`.
4. `release.yml` runs on the tag push. Its first step verifies the tag matches the csproj
   `Version` and fails fast with a clear error if you forgot step 1 — it will not silently publish
   a mismatched release. The packaging step's `.msix` file name is what gets uploaded and what
   the `.appinstaller` `MainPackage` URI uses.

## Installing / updating as a user

**First install:** download the certificate and the App Installer file from the stable URLs
above (or from the latest release page, which hosts the same files). Trust the certificate once
(as Administrator):

```powershell
Import-Certificate -FilePath PlaySphere.App.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
```

Then double-click `PlaySphere.App.appinstaller` to install. The file's `Uri` is the stable
App Installer URL, so Windows checks that URL for updates automatically (once every 24 hours by
default, and also on each app launch) even if the file was saved from a specific release page.

**Skipping certificate trust results in Windows refusing the install** with
`0x800B010A - the certificate could not be verified` — this is expected for a self-signed
certificate; see `docs/security/code-signing.md`.

## The re-trust-every-release limitation

`release.yml` generates a **new throwaway self-signed certificate on every run** (via
`tools/create-dev-signing-cert.ps1`, which creates one if none exists on that ephemeral runner —
and a fresh GitHub Actions runner never has one). This means:

- Every release is signed by a *different* certificate, even though all certificates share the
  same Subject (`CN=sarayunova`) required to match `Package.appxmanifest`.
- A machine that trusted a previous release's certificate must **trust the new one again** before
  App Installer's automatic update will succeed — the old trust doesn't carry over. The current
  certificate is `PlaySphere.App.cer` on the latest release (stable URL above).
- This is a known, accepted limitation of this phase, not a bug. ADR-0010 explicitly defers the
  real signing-certificate decision (a paid CA certificate, Azure Trusted Signing, or Microsoft
  Store signing); once one of those is chosen, this limitation goes away because the certificate
  (and its trust chain) stops changing between releases.

## What isn't wired up yet

- No cadence or branch-protection rule enforces when tags get cut — this is a manual, deliberate
  action by whoever is doing the release.
- The GitHub Release's auto-generated notes (`--generate-notes`) are just the commit list between
  tags; nothing curates them into user-facing release notes yet.
