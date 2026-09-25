using System;
using Microsoft.Extensions.DependencyInjection;
using PlaySphere.Application.Visualizer;
using PlaySphere.Visualizer;

namespace PlaySphere.Audio;

public static class AudioServiceCollectionExtensions
{
    /// <summary>
    /// Registers a WASAPI <see cref="IVisualizerAudioSource"/> (ADR-0009) that feeds the audio
    /// Windows mixes to the output endpoint to the visualizer while libVLC keeps outputting to the
    /// speakers. Call AFTER <c>AddPlaySphereVisualizer()</c>. It wraps the concrete
    /// <see cref="PcmAudioSource"/> (which drives the analyser + latest-frame channel) in a
    /// <see cref="WasapiLoopbackCaptureSource"/>, overriding the default
    /// <see cref="IVisualizerAudioSource"/>; if the loopback capture cannot start it degrades
    /// gracefully to an inactive source (ADR-0008 PCM-tap remains the non-Windows fallback).
    /// </summary>
    public static IServiceCollection AddPlaySphereAudio(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<PcmAudioSource>(sp =>
        {
            var analyzer = sp.GetRequiredService<IAudioAnalyzer>();
            var sink = sp.GetRequiredService<ILatestAnalysisFrameProvider>();
            return new PcmAudioSource(analyzer, sink);
        });

        services.AddSingleton<IVisualizerAudioSource>(sp =>
        {
            // The concrete PcmAudioSource (which drives the analyser + latest-frame channel) is the
            // inner sink the loopback wrapper forwards captured audio into.
            var inner = sp.GetRequiredService<PcmAudioSource>();
            return new WasapiLoopbackCaptureSource(inner);
        });

        return services;
    }

    /// <summary>
    /// Registers the stereo-balance secondary audio session (ADR-0011 Stage 2). Not wired into
    /// <c>PlayerViewModel</c>/the UI yet — this only makes <see cref="StereoBalanceAudioSession"/>
    /// resolvable for that follow-up slice.
    /// </summary>
    public static IServiceCollection AddPlaySphereStereoBalance(this IServiceCollection services, StereoBalanceAudioOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(options ?? new StereoBalanceAudioOptions());
        services.AddSingleton<StereoBalanceAudioSession>();

        return services;
    }
}
