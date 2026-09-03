using System;

namespace PlaySphere.Domain.AI;

/// <summary>
/// Configuration for the OpenAI-backed <c>IAiMediaService</c> (PICKLE.md §38-§39). The API key is
/// deliberately NOT here — it belongs only in Windows secure credential storage via
/// <c>IAiCredentialStore</c> (PICKLE.md §40); never in JSON settings or this options object.
/// </summary>
public sealed class AiServiceOptions
{
    /// <summary>OpenAI-compatible API base URL. Defaults to the public <c>https://api.openai.com/</c>.</summary>
    public Uri? BaseUrl { get; set; }

    /// <summary>Model id for chat (summaries, chapters, ask). Never hard-coded across the app; this is the single place it is resolved.</summary>
    public string? ChatModel { get; set; }

    /// <summary>Model id for audio transcription.</summary>
    public string? TranscriptionModel { get; set; }

    /// <summary>Request timeout for any single provider call.</summary>
    public TimeSpan? Timeout { get; set; }
}
