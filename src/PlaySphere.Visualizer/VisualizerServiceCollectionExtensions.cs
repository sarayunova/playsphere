using Microsoft.Extensions.DependencyInjection;
using PlaySphere.Application.Visualizer;

namespace PlaySphere.Visualizer;

public static class VisualizerServiceCollectionExtensions
{
    /// <summary>
    /// Registers the visualizer DSP core (PICKLE.md §33): the <see cref="IAudioAnalyzer"/>,
    /// the latest-frame <see cref="ILatestAnalysisFrameProvider"/> channel, and the
    /// <see cref="IVisualizerAudioSource"/> PCM source that drives them.
    /// </summary>
    public static IServiceCollection AddPlaySphereVisualizer(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IAudioAnalyzer, AudioAnalyzer>();
        services.AddSingleton<ILatestAnalysisFrameProvider, LatestAnalysisFrameChannel>();
        services.AddSingleton<IVisualizerAudioSource, PcmAudioSource>();
        return services;
    }
}
