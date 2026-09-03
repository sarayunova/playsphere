using System;
using System.Threading;
using PlaySphere.Application.AI;

namespace PlaySphere.UnitTests.AI;

/// <summary>In-memory <see cref="IAiCredentialStore"/> for service tests (never touches the OS vault).</summary>
internal sealed class MemoryCredentialStore : IAiCredentialStore
{
    private string? _key;

    public bool HasApiKey() => _key is not null;

    public string? GetApiKey() => _key;

    public void SetApiKey(string? apiKey)
    {
        _key = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
    }

    public void ClearApiKey() => _key = null;
}
