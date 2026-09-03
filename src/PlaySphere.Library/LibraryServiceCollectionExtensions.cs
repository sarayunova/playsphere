using Microsoft.Extensions.DependencyInjection;
using PlaySphere.Application.Library;

namespace PlaySphere.Library;

public static class LibraryServiceCollectionExtensions
{
    /// <summary>Registers <see cref="ILibraryScanner"/>. Requires <see cref="IMediaLibraryRepository"/> to already be registered.</summary>
    public static IServiceCollection AddPlaySphereLibraryScanning(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<ILibraryScanner, LibraryScanner>();
        return services;
    }

    /// <summary>
    /// Registers M3U/M3U8 import and export (PICKLE.md §25). Requires <see cref="IMediaLibraryRepository"/>
    /// to already be registered.
    /// </summary>
    public static IServiceCollection AddPlaySphereM3u(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<PlaySphere.Application.Library.IM3uImporter, M3uImporter>();
        services.AddSingleton<PlaySphere.Application.Library.IM3uExporter, M3uExporter>();
        return services;
    }
}
