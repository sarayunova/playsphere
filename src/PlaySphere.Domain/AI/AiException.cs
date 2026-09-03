using System;

namespace PlaySphere.Domain.AI;

/// <summary>
/// Thrown by <c>PlaySphere.Application.AI.IAiMediaService</c> when an AI provider call fails for a
/// non-cancellation reason (missing key, transport/HTTP error, provider error, malformed response).
/// Distinct from <see cref="OperationCanceledException"/>, which is used for user-initiated
/// cancellation and must not be swallowed by callers.
/// </summary>
public sealed class AiException : Exception
{
    public AiException(string message) : base(message) { }

    public AiException(string message, Exception inner) : base(message, inner) { }
}
