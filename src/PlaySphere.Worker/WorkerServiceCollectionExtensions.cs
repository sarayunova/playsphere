using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PlaySphere.Application.Media;

namespace PlaySphere.Worker;

/// <summary>DI wiring for the background conversion queue (PICKLE.md §30).</summary>
public static class WorkerServiceCollectionExtensions
{
    /// <summary>
    /// Registers the serial background <see cref="IConversionQueue"/> singleton. The converter,
    /// exporter and probe used by the queue are resolved from the container — they must be
    /// registered by the caller (<c>AddPlaySphereConversion</c> / <c>AddPlaySphereClipExport</c> /
    /// <c>AddPlaySphereMediaProbe</c>); any of them being absent merely disables the jobs that need it.
    /// </summary>
    public static IServiceCollection AddPlaySphereConversionQueue(this IServiceCollection services)
    {
        services.TryAddSingleton<IConversionQueue, ConversionQueue>();
        return services;
    }
}