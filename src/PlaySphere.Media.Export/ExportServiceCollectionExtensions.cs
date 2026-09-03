using Microsoft.Extensions.DependencyInjection;
using PlaySphere.Application.Media;

namespace PlaySphere.Media.Export;

public static class ExportServiceCollectionExtensions
{
    /// <summary>
    /// Registers the bounded ffmpeg-backed <see cref="IMediaClipExporter"/>. Passing
    /// <paramref name="options"/> (optional) overrides the executable path and export limits.
    /// </summary>
    public static IServiceCollection AddPlaySphereClipExport(this IServiceCollection services, FfMpegExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (options is not null)
        {
            services.AddSingleton(options);
        }
        services.AddSingleton<IMediaClipExporter, FfMpegClipExporter>();
        return services;
    }

    /// <summary>
    /// Registers the bounded ffmpeg-backed <see cref="IMediaConverter"/>. Passing
    /// <paramref name="options"/> (optional) overrides the executable path and conversion limits.
    /// </summary>
    public static IServiceCollection AddPlaySphereConversion(this IServiceCollection services, FfMpegConvertOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (options is not null)
        {
            services.AddSingleton(options);
        }
        services.AddSingleton<IMediaConverter, FfMpegMediaConverter>();
        return services;
    }

    /// <summary>
    /// Registers the bounded ffmpeg-backed <see cref="IMediaThumbnailProvider"/> (PS-704 hover
    /// thumbnails). Passing <paramref name="options"/> (optional) overrides the executable path,
    /// grid limits and bounds. Gracefully unavailable when the executable is not resolvable.
    /// </summary>
    public static IServiceCollection AddPlaySphereThumbnails(this IServiceCollection services, ThumbnailStripOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (options is not null)
        {
            services.AddSingleton(options);
        }
        services.AddSingleton<IMediaThumbnailProvider, FfMpegThumbnailProvider>();
        return services;
    }

    /// <summary>
    /// Registers the bounded ffmpeg-backed <see cref="IWaveformProvider"/> (PS-705 timeline waveform).
    /// Passing <paramref name="options"/> (optional) overrides the executable path and strip bounds.
    /// Gracefully unavailable when the executable is not resolvable.
    /// </summary>
    public static IServiceCollection AddPlaySphereWaveform(this IServiceCollection services, WaveformOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (options is not null)
        {
            services.AddSingleton(options);
        }
        services.AddSingleton<IWaveformProvider, FfMpegWaveformProvider>();
        return services;
    }

    /// <summary>
    /// Registers the bounded ffmpeg-backed <see cref="IAudioExtractor"/> (AI-02). Passing
    /// <paramref name="options"/> (optional) overrides the executable path and extraction limits.
    /// </summary>
    public static IServiceCollection AddPlaySphereAudioExtraction(this IServiceCollection services, FfMpegAudioExtractOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (options is not null)
        {
            services.AddSingleton(options);
        }
        services.AddSingleton<IAudioExtractor, FfMpegAudioExtractor>();
        return services;
    }
}