using System.Threading;
using System.Threading.Tasks;

namespace PlaySphere.Application.AI;

/// <summary>
/// Secure storage for the OpenAI API key (PICKLE.md §40: secrets must use Windows secure credential
/// storage, never settings JSON). Implementations must never expose the secret to I/O that leaves the
/// device other than the configured provider call, and must never write it to settings/JSON.
/// </summary>
public interface IAiCredentialStore
{
    /// <summary>True when a non-empty API key is available.</summary>
    bool HasApiKey();

    /// <summary>The stored API key, or <see langword="null"/> when none is configured.</summary>
    string? GetApiKey();

    /// <summary>Stores/replaces the API key. Pass <see langword="null"/> to clear it.</summary>
    void SetApiKey(string? apiKey);

    /// <summary>Removes the stored key.</summary>
    void ClearApiKey();
}
