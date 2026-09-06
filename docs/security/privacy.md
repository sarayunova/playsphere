# Privacy (Phase 10)

This documents what LiVi PlaySphere Studio actually does with data, based on the code as it
exists today (verified by reading `PlaySphere.AI`'s network and credential-storage code, and
by confirming no other project makes network calls or includes any telemetry/analytics/crash-
reporting library). It is a factual description of current behavior, not a reviewed legal privacy
policy - PICKLE.md Phase 10 calls for privacy documentation, and this is that documentation, but
publishing it as the app's official privacy policy should get a legal/product review first.

## Local-only by default

PlaySphere is offline-first. With AI features untouched, nothing it does reaches the network.
Everything it stores lives under `%LOCALAPPDATA%\LiVi PlaySphere Studio\` on the local machine:
preferences (`preferences.json`), the media library database, thumbnail/waveform caches, and
diagnostic logs (e.g. `visualizer-audio.log`). None of this is transmitted anywhere by the app.

There is no telemetry, analytics, or crash-reporting SDK anywhere in the codebase - confirmed by
searching all source projects, not just asserted.

## The one exception: opt-in AI features (PlaySphere.AI)

`PlaySphere.AI` calls the OpenAI API (`api.openai.com` by default - configurable via
`AiServiceOptions.BaseUrl`, so a build could point it elsewhere, though nothing in this repo does)
for four features, each triggered only by explicit user action, and only once a user has entered
their own API key in Settings:

| Feature | What gets sent to OpenAI |
|---|---|
| Transcribe | The raw bytes of the local audio/video file being transcribed |
| Generate chapters | The transcript text (produced by transcription, above) |
| Summarize | The transcript text |
| Ask the media | The transcript text plus the user's typed question |

No media file, transcript, or question is ever sent unless the corresponding feature is invoked
by the user - `OpenAiMediaService` throws rather than sending anything if no API key is
configured (`AiServiceCollectionExtensions`'s registration comment: "AI is strictly opt-in - no
provider call happens unless a feature calls the service after explicit user action").

**Once sent, that data is subject to OpenAI's own privacy policy and data-retention practices,
not this app's** - check OpenAI's current terms directly, since they're outside this app's
control and can change independently of this document.

## API key storage

The OpenAI API key is stored via Windows Credential Manager (`WindowsCredentialStore` /
`Windows.Security.Credentials.PasswordVault`), OS-encrypted per-user - never written to
`preferences.json` or any other plaintext file. If the credential vault is unavailable, the key
falls back to an in-memory-only copy for that session (still never persisted to disk). Clearing
the key in Settings removes it from the vault.

## Network capability

`Package.appxmanifest` declares the `internetClient` MSIX capability *only* because of this AI
path - it is otherwise unused. `runFullTrust` is also declared, but that governs local API access
(native libVLC, ffmpeg/ffprobe subprocesses), not network access.

## What this document does not cover

- Any data OpenAI itself collects, retains, or uses once PlaySphere sends it a request - that is
  governed by OpenAI's policies, not this app.
- A model for future features - if a future feature adds another network call or persists data
  differently, this document needs to be updated to match, not assumed to still be accurate.
