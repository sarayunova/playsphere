---
name: playsphere-security
description: Untrusted files, path safety, subprocess invocation, credential safety and native dependency review for PlaySphere.
---

# PlaySphere Security

## Rules
Treat media files, subtitles, playlists, URLs, metadata, thumbnails, archives and streams as **untrusted**.

- Path canonicalization, file-size limits, parser limits, URL-scheme validation.
- FFmpeg processes: timeouts, cancellation, no shell injection.
- API keys/OpenAI secrets in Windows secure credential storage only — never settings JSON.
- Bounded logs, secret redaction, dependency vulnerability checks.
- Privacy: no silent uploads; AI is opt-in; never upload full video when audio suffices.
- Run a security review before Phase 10 hardening.