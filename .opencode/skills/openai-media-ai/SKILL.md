---
name: openai-media-ai
description: Transcription, Responses API, embeddings, transcript retrieval, timestamp evidence and safe AI integration for PlaySphere.
---

# OpenAI Media AI

## Scope
All OpenAI access lives in `PlaySphere.AI` behind `IAiMediaService` (PICKLE.md §38/§39). No UI ViewModel instantiates an OpenAI client directly. UI/modules depend only on the `Application.AI` / `Domain.AI` contracts.

## Boundary discipline
- `OpenAiMediaService` is the sole provider HTTP implementation. It is transport-agnostic over the OpenAI REST protocol (chat/completions, audio/transcriptions) and takes an injected `HttpMessageHandler` for headless tests.
- The API key resolves from `IAiCredentialStore` (Windows secure storage via `WindowsCredentialStore`). NEVER persist or log the key; never store it in settings JSON (AGENTS rule 8, §57).
- Every call is cancellable (linked CTS + timeout) and failures surface as `AiException`.
- AI is optional: disabled unless explicitly enabled, and AI failures never interfere with playback.

## Privacy (§40)
- Before uploading audio/transcript: require explicit user action, explain what leaves the device, allow cancellation, obey Local Only mode.
- Never upload full video when audio is sufficient.

## Ask-the-Media (§41)
Answers must return clickable timestamp evidence (`AiKeyPoint.start`) from verbatim transcript seconds. Do not claim support unless transcript evidence exists.

## Phase 9 roadmap (AI-01..AI-09)
Backend service (AI-01/02/03: boundary, audio extraction, transcription) is scaffolded here. Remaining: transcript UI, timestamp search, automatic chapters, summaries, Ask-the-Media wiring, subtitle translation.

## Testing convention
Contract tests via a fake `HttpMessageHandler` routing canned JSON responses (see `OpenAiMediaServiceTests`). Keep `ConfigureAwait(false)` out of test methods (xUnit1030).
