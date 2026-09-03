using Microsoft.Extensions.DependencyInjection;
using PlaySphere.Application.Visualizer;
using PlaySphere.Audio;
using PlaySphere.Visualizer;
using Xunit;

namespace PlaySphere.UnitTests;

/// <summary>
/// DI registration for the ADR-0009 WASAPI device-loopback visualizer source. The NAudio recorder
/// itself needs a real Windows audio device (not verifiable headlessly), so this verifies the wiring
/// shape: after <c>AddPlaySphereAudio()</c> the resolved <see cref="IVisualizerAudioSource"/> is the
/// loopback wrapper around the existing PCM source, and Stop() is safe on an unstarted source.
/// </summary>
public sealed class AudioServiceCollectionRegistrationTests
{
    [Fact]
    public void AddPlaySphereAudio_wires_loopback_wrapper_over_pcm_source()
    {
        var services = new ServiceCollection();
        services.AddPlaySphereVisualizer();
        services.AddPlaySphereAudio();
        using var provider = services.BuildServiceProvider();

        var source = provider.GetRequiredService<IVisualizerAudioSource>();

        // The ADR-0009 wrapper is now the registered IVisualizerAudioSource; it forwards to the
        // concrete PcmAudioSource which drives the analyser/latest-frame channel.
        var loopback = Assert.IsType<WasapiLoopbackCaptureSource>(source);

        // Stop() on an unstarted source is a safe no-op (must not throw).
        loopback.Stop();
    }

    [Fact]
    public void AddPlaySphereAudio_preserves_pcm_source_pipeline()
    {
        var services = new ServiceCollection();
        services.AddPlaySphereVisualizer();
        services.AddPlaySphereAudio();
        using var provider = services.BuildServiceProvider();

        var inner = provider.GetRequiredService<PcmAudioSource>();

        // Pushing a full 2048-sample mono window must publish a frame to the latest-frame channel,
        // proving the analyser pipeline is intact behind the wrapper.
        inner.PushSamples(new float[2048], 44100, 1);
        Assert.NotNull(((ILatestAnalysisFrameProvider)provider.GetRequiredService<ILatestAnalysisFrameProvider>()).TryReadLatest());
    }
}