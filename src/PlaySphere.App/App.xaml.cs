using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using PlaySphere.AI;
using PlaySphere.Audio;
using PlaySphere.Infrastructure.Library;
using PlaySphere.Infrastructure.Preferences;
using PlaySphere.Library;
using PlaySphere.Media.Export;
using PlaySphere.Media.Probe;
using PlaySphere.Playback.LibVLC;
using PlaySphere.UI;
using PlaySphere.UI.Services;
using PlaySphere.Visualizer;
using PlaySphere.Worker;

namespace PlaySphere.App;

// Fully qualified: PlaySphere.Application (a project namespace, now that the Library ticket
// added types to it) shadows the bare "Application" simple-name lookup for Microsoft.UI.Xaml.Application
// here, since both are reachable as "Application" from the enclosing PlaySphere namespace.
public partial class App : Microsoft.UI.Xaml.Application
{
    /// <summary>Application-level DI container. Accessible to pages via the service locator pattern.</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
        UnhandledException += App_UnhandledException;

        // Unpackaged app (ADR-0001): use the classic per-user AppData path, not
        // Windows.Storage.ApplicationData, which requires package identity.
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LiVi PlaySphere Studio",
            "library.db");

        Services = new ServiceCollection()
            .AddLogging(builder => builder.AddDebug().SetMinimumLevel(LogLevel.Information))
            .AddPlaySpherePlayback(ConfigureLibVlcOptions())
            .AddPlaySphereLibrary(dbPath)
            .AddPlaySphereLibraryScanning()
            .AddPlaySphereM3u()
            .AddPlaySphereMediaProbe(ConfigureFfProbeOptions())
            .AddPlaySphereClipExport(ConfigureFfMpegExportOptions())
            .AddPlaySphereConversion(ConfigureFfMpegConvertOptions())
            .AddPlaySphereThumbnails(ConfigureFfMpegThumbnailOptions())
            .AddPlaySphereWaveform(ConfigureFfMpegWaveformOptions())
            .AddPlaySphereAudioExtraction(ConfigureFfMpegAudioExtractOptions())
            .AddPlaySphereConversionQueue()
            .AddPlaySpherePreferences()
            .AddPlaySphereVisualizer()
            .AddPlaySphereAudio()
            .AddPlaySphereAi(ConfigureAiOptions())
            .AddSingleton<IWindowService, AppWindowService>()
            .BuildServiceProvider();

        Services.MigrateLibraryDatabase();

        PlaySphereServices.Configure(Services);
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        LogCrash(e.Exception);
    }

    private static void LogCrash(Exception exception)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LiVi PlaySphere Studio");
            Directory.CreateDirectory(dir);
            File.AppendAllText(
                Path.Combine(dir, "crash.log"),
                $"[{DateTime.UtcNow:o}] {exception}\n");
        }
        catch
        {
            // Logging failures must never take the app down.
        }
    }

    /// <summary>
    /// Points the FFprobe probe at the checked-in <c>tools\ffmpeg\ffprobe.exe</c> when running
    /// from a repository checkout, so scans populate real technical metadata in dev. Returns
    /// <c>null</c> when it cannot be found — the probe then degrades gracefully (looks next to
    /// the app, otherwise skips probing) rather than failing the scan.
    /// </summary>
    private static FfProbeOptions? ConfigureFfProbeOptions()
    {
        var ffprobe = FindUpwards("tools", "ffmpeg", "ffprobe.exe");
        return ffprobe is null ? null : new FfProbeOptions { ExecutablePath = ffprobe };
    }

    /// <summary>
    /// Points the clip exporter at the checked-in <c>tools\ffmpeg\ffmpeg.exe</c> when running from a
    /// repository checkout, so Mark In/Out → Export Clip works in dev. Returns <c>null</c> when it
    /// cannot be found — the exporter then degrades gracefully (looks next to the app, otherwise
    /// reports export unavailable) rather than failing at startup.
    /// </summary>
    private static FfMpegExportOptions? ConfigureFfMpegExportOptions()
    {
        var ffmpeg = FindUpwards("tools", "ffmpeg", "ffmpeg.exe");
        return ffmpeg is null ? null : new FfMpegExportOptions { ExecutablePath = ffmpeg };
    }

    /// <summary>
    /// Points the media converter at the checked-in <c>tools\ffmpeg\ffmpeg.exe</c> when running from a
    /// repository checkout, so File → Convert works in dev. Returns <c>null</c> when it cannot be
    /// found — the converter then degrades gracefully (looks next to the app, otherwise reports
    /// conversion unavailable) rather than failing at startup.
    /// </summary>
    private static FfMpegConvertOptions? ConfigureFfMpegConvertOptions()
    {
        var ffmpeg = FindUpwards("tools", "ffmpeg", "ffmpeg.exe");
        return ffmpeg is null ? null : new FfMpegConvertOptions { ExecutablePath = ffmpeg };
    }

    /// <summary>
    /// Points the hover-thumbnail provider at the checked-in <c>tools\ffmpeg\ffmpeg.exe</c> when
    /// running from a repository checkout, so timeline scrub previews work in dev. Returns
    /// <c>null</c> when it cannot be found — previews then degrade gracefully (no hover thumbnails)
    /// rather than failing at startup.
    /// </summary>
private static ThumbnailStripOptions? ConfigureFfMpegThumbnailOptions()
    {
        var ffmpeg = FindUpwards("tools", "ffmpeg", "ffmpeg.exe");
        return ffmpeg is null ? null : new ThumbnailStripOptions { ExecutablePath = ffmpeg };
    }

    private static WaveformOptions? ConfigureFfMpegWaveformOptions()
    {
        var ffmpeg = FindUpwards("tools", "ffmpeg", "ffmpeg.exe");
        return ffmpeg is null ? null : new WaveformOptions { ExecutablePath = ffmpeg };
    }

    /// <summary>
    /// Points the audio extractor at the checked-in <c>tools\ffmpeg\ffmpeg.exe</c> when running from a
    /// repository checkout, so AI transcription can extract audio in dev. Returns <c>null</c> when it
    /// cannot be found — the extractor then degrades gracefully (looks next to the app, otherwise
    /// reports extraction unavailable).
    /// </summary>
    private static FfMpegAudioExtractOptions? ConfigureFfMpegAudioExtractOptions()
    {
        var ffmpeg = FindUpwards("tools", "ffmpeg", "ffmpeg.exe");
        return ffmpeg is null ? null : new FfMpegAudioExtractOptions { ExecutablePath = ffmpeg };
    }

    /// <summary>
    /// Configures the OpenAI <c>IAiMediaService</c> boundary (PICKLE.md §39). Defaults the base URL
    /// and models; the API key is resolved only from Windows secure credential storage at call time
    /// (never here, never in settings JSON — §40). Returns the shared default options.
    /// </summary>
    private static PlaySphere.Domain.AI.AiServiceOptions ConfigureAiOptions()
    {
        return new PlaySphere.Domain.AI.AiServiceOptions
        {
            BaseUrl = new System.Uri("https://api.openai.com/"),
            ChatModel = "gpt-4o-mini",
            TranscriptionModel = "whisper-1",
        };
    }

    /// <summary>
    /// Enables libVLC's native message log to a file (for diagnosing real playback failures such
    /// as "Play() returned false"). Removed/relaxed before any production release if desired; it
    /// exists so real audio-enabled playback errors are capturable headlessly.
    /// </summary>
    private static Action<PlaySphere.Playback.LibVLC.LibVlcOptions> ConfigureLibVlcOptions()
    {
        var logFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LiVi PlaySphere Studio",
            "libvlc.log");
        return o =>
        {
            o.Verbosity = 2;
            o.LogFilePath = logFile;
        };
    }

    private static string? FindUpwards(params string[] segments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 8 && dir is not null; depth++, dir = dir.Parent)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var window = new MainWindow();

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        if (hwnd != IntPtr.Zero)
        {
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
            if (appWindow != null && Services.GetService<IWindowService>() is AppWindowService windowService)
            {
                windowService.Attach(appWindow);
            }
        }

        window.Activate();
    }
}