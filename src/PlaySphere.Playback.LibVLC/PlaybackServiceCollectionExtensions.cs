using LibVLCSharp.Shared;
using Microsoft.Extensions.DependencyInjection;
using PlaySphere.Playback.Abstractions;
using LibVLCLib = LibVLCSharp.Shared.LibVLC;

namespace PlaySphere.Playback.LibVLC;

public static class PlaybackServiceCollectionExtensions
{
    /// <summary>
    /// Registers the libVLC playback stack:
    /// shared <see cref="LibVLCLib"/> (with <see cref="LibVlcOptions"/>), a shared
    /// <see cref="MediaPlayer"/> and <see cref="IMediaPlaybackEngine"/>.
    /// </summary>
    public static IServiceCollection AddPlaySpherePlayback(
        this IServiceCollection services,
        Action<LibVlcOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(_ =>
        {
            var options = new LibVlcOptions();
            configure?.Invoke(options);
            return options;
        });

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<LibVlcOptions>();
            return LibVlcBootstrap.CreateLibVlc(options);
        });

        services.AddSingleton<MediaPlayer>(sp => new MediaPlayer(sp.GetRequiredService<LibVLCLib>()));

        // Singleton: one engine per app lifetime matches one shared MediaPlayer/LibVLC instance.
        // Multi-player support (Phase 5+) will require a factory/pool pattern instead.
        services.AddSingleton<IMediaPlaybackEngine, LibVlcPlaybackEngine>();
        return services;
    }
}