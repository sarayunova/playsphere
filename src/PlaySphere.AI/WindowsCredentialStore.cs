using System;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Security.Credentials;
using PlaySphere.Application.AI;

namespace PlaySphere.AI;

/// <summary>
/// Stores the OpenAI API key in the Windows Credential Manager (PICKLE.md §40: secrets must use
/// Windows secure credential storage, never settings JSON). Uses the unpackaged-app credential
/// resource conventions; on platforms without the Windows Credential Vault this degrades to an
/// in-memory fallback (never persisted to disk).
/// </summary>
public sealed class WindowsCredentialStore : IAiCredentialStore
{
    private const string ResourceTarget = "LiViPlaySphereStudio/OpenAI";
    private readonly string _userName;
    private readonly object _gate = new();
    private string? _memoryFallback;

    public WindowsCredentialStore(string? userName = null)
    {
        _userName = userName ?? Environment.UserName;
    }

    public bool HasApiKey() => GetApiKey() is not null;

    public string? GetApiKey()
    {
        lock (_gate)
        {
            return ReadFromVault() ?? _memoryFallback;
        }
    }

    public void SetApiKey(string? apiKey)
    {
        lock (_gate)
        {
            apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
            if (apiKey is null)
            {
                ClearApiKey();
                return;
            }

            try
            {
                DeleteVaultEntry();
                var vault = new PasswordVault();
                vault.Add(new PasswordCredential(ResourceTarget, _userName, apiKey));
                _memoryFallback = null;
            }
            catch
            {
                // Vault unavailable (e.g. non-Windows store) — keep an in-memory copy so the app
                // still functions for the session, but never persist the secret to disk here.
                _memoryFallback = apiKey;
            }
        }
    }

    public void ClearApiKey()
    {
        lock (_gate)
        {
            _memoryFallback = null;
            try
            {
                DeleteVaultEntry();
            }
            catch
            {
                // Ignore vault teardown failures — in-memory copy is already cleared.
            }
        }
    }

    private string? ReadFromVault()
    {
        try
        {
            var vault = new PasswordVault();
            var creds = vault.RetrieveAll();
            foreach (var c in creds)
            {
                if (c.Resource == ResourceTarget && c.UserName == _userName)
                {
                    c.RetrievePassword();
                    return string.IsNullOrWhiteSpace(c.Password) ? null : c.Password;
                }
            }
        }
        catch
        {
            // No matching credential or vault unavailable.
        }
        return null;
    }

    private void DeleteVaultEntry()
    {
        try
        {
            var vault = new PasswordVault();
            var existing = vault.RetrieveAll();
            foreach (var c in existing)
            {
                if (c.Resource == ResourceTarget)
                {
                    vault.Remove(c);
                }
            }
        }
        catch
        {
            // Vault unavailable — nothing to remove.
        }
    }
}
