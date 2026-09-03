using PlaySphere.Playback.Abstractions;

namespace PlaySphere.UI;

/// <summary>
/// Minimal ambient service locator for UI pages that need access to services
/// registered at app startup. Populated by <c>App</c> before any navigation.
///
/// Using a service locator here is a deliberate tradeoff: WinUI 3 page instantiation
/// is driven by <c>Frame.Navigate(typeof(Page))</c>, which does not support constructor
/// injection. The UI project must not reference PlaySphere.App (circular), so we keep a
/// thin, resettable reference here. Replace with a full DI-driven page factory in a
/// later phase if needed.
/// </summary>
public static class PlaySphereServices
{
    private static IServiceProvider? _provider;

    /// <summary>
    /// Sets the service provider once at app startup. Must be called before navigation.
    /// </summary>
    public static void Configure(IServiceProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    /// <summary>Gets a required service, throwing if not registered or not configured.</summary>
    public static T GetRequired<T>() where T : notnull
    {
        if (_provider is null)
        {
            throw new InvalidOperationException(
                $"PlaySphereServices has not been configured. Call Configure(IServiceProvider) at app startup.");
        }

        var service = _provider.GetService(typeof(T));
        if (service is null)
        {
            throw new InvalidOperationException($"Service {typeof(T).Name} is not registered.");
        }

        return (T)service;
    }

    /// <summary>Gets an optional service, returning null if not registered or not configured.</summary>
    public static T? GetOptional<T>() where T : class
    {
        return _provider?.GetService(typeof(T)) as T;
    }
}
