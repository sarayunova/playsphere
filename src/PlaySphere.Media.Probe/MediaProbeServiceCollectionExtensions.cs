using Microsoft.Extensions.DependencyInjection;
using PlaySphere.Application.Media;

namespace PlaySphere.Media.Probe;

public static class MediaProbeServiceCollectionExtensions
{
    /// <summary>
    /// Registers the FFprobe-backed <see cref="IMediaProbe"/>. Passing <paramref name="options"/>
    /// (optional) overrides the executable path and per-probe limits.
    /// </summary>
    public static IServiceCollection AddPlaySphereMediaProbe(this IServiceCollection services, FfProbeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (options is not null)
        {
            services.AddSingleton(options);
        }
        services.AddSingleton<IMediaProbe, FfProbeMediaProbe>();
        return services;
    }
}
