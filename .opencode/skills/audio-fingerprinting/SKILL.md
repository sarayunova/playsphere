---
name: audio-fingerprinting
description: Perceptual audio hashing and identification considerations for PlaySphere.
---

# Audio Fingerprinting

## Context
The media library uses **file identity fingerprinting** (`MediaFileFingerprinter`: length + UTC mtime + SHA-256 over three sampled 64KB chunks, §24) — that is file identity, NOT perceptual audio fingerprinting. It is deliberately lightweight (never whole-file) and indexes the library.

## Separation
Perceptual audio fingerprinting (identifying *what* a recording is, independent of file) is a distinct, future capability. It is not required for any Phase 0–8 feature. Treat it as a P2 research item — no current schema or module exists for it.

## Principles (when it is eventually built)
- Offline-first: fingerprinting must run locally, never upload media.
- Keep it bounded like the existing file fingerprinter (§43); never scan all drives.
- Follow the module pattern (Domain model + Application contract + bounded implementation).
