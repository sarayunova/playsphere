using Microsoft.Extensions.DependencyInjection;
using PlaySphere.Application.Diagnostics;
using PlaySphere.Application.Preferences;

namespace PlaySphere.Infrastructure.Preferences;

public static class PreferencesServiceCollectionExtensions
{
    /// <summary>
    /// Registers a JSON-file <see cref="IPreferencesStore"/> at
    /// <c>%LOCALAPPDATA%\LiVi PlaySphere Studio\preferences.json</c> (unpackaged app per
    /// ADR-0001), or at <paramref name="filePath"/> when provided (used by tests).
    /// </summary>
    public static IServiceCollection AddPlaySpherePreferences(this IServiceCollection services, string? filePath = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var path = filePath ?? Path.Combine(
            DiagnosticReportLocations.DefaultDataDirectory(),
            "preferences.json");

        services.AddSingleton<IPreferencesStore>(_ => new JsonPreferencesStore(path));

        return services;
    }
}
