using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using PlaySphere.AI;
using PlaySphere.Audio;
using PlaySphere.Application.Diagnostics;
using PlaySphere.Infrastructure.Diagnostics;
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
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash(e.ExceptionObject as Exception, "AppDomain");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogCrash(e.Exception, "UnobservedTask");
            e.SetObserved();
        };

        // Unpackaged app (ADR-0001): use the classic per-user AppData path, not
        // Windows.Storage.ApplicationData, which requires package identity.
        var dbPath = Path.Combine(DiagnosticReportLocations.DefaultDataDirectory(), "library.db");

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
            .AddPlaySphereDiagnostics(new DiagnosticReportOptions
            {
                // Shipped tester default. Pass a different SupportEmail here to retarget a build.
                SupportEmail = DiagnosticReportDefaults.SupportEmail,
            })
            .AddPlaySphereVisualizer()
            .AddPlaySphereAudio()
            .AddPlaySphereStereoBalance(ConfigureStereoBalanceOptions())
            .AddPlaySphereAi(ConfigureAiOptions())
            .AddSingleton<IWindowService, AppWindowService>()
            .BuildServiceProvider();

        Services.MigrateLibraryDatabase();

        PlaySphereServices.Configure(Services);
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        LogCrash(e.Exception, "XamlUI");
    }

    /// <summary>
    /// Crash diagnostics (Phase 10, PICKLE.md §52): a local-only, append-only log at
    /// <c>%LOCALAPPDATA%\LiVi PlaySphere Studio\crash.log</c> - never transmitted anywhere,
    /// consistent with this app having no telemetry (see docs/security/privacy.md). Covers all
    /// three places .NET surfaces an otherwise-unlogged unhandled exception: the XAML UI thread
    /// (<see cref="UnhandledException"/>), any other thread (<see cref="AppDomain.UnhandledException"/> -
    /// previously uncovered here, meaning a background-thread crash left zero trace), and
    /// unobserved Task exceptions (diagnostic only; these don't crash the process).
    /// </summary>
    private static void LogCrash(Exception? exception, string source)
    {
        if (exception is null)
        {
            return;
        }

        try
        {
            var dir = DiagnosticReportLocations.DefaultDataDirectory();
            Directory.CreateDirectory(dir);
            var logPath = Path.Combine(dir, DiagnosticLogCatalog.CrashLogFileName);

            // Cap unbounded growth from a repeated crash loop: keep one rotated copy.
            const long maxBytes = 5 * 1024 * 1024;
            if (File.Exists(logPath) && new FileInfo(logPath).Length > maxBytes)
            {
                File.Copy(logPath, Path.Combine(dir, DiagnosticLogCatalog.CrashLogRotationFileName), overwrite: true);
                File.Delete(logPath);
            }

            var appVersion = GetAppVersion();
            File.AppendAllText(
                logPath,
                $"[{DateTime.UtcNow:o}] source={source} appVersion={appVersion} os={Environment.OSVersion.VersionString}\n{exception}\n\n");
        }
        catch
        {
            // Logging failures must never take the app down.
        }
    }

    private static string GetAppVersion()
    {
        try
        {
            var packageVersion = Windows.ApplicationModel.Package.Current.Id.Version;
            return $"{packageVersion.Major}.{packageVersion.Minor}.{packageVersion.Build}.{packageVersion.Revision}";
        }
        catch
        {
            // Package.Current requires MSIX package identity - throws when running unpackaged
            // (e.g. `dotnet run` in dev). Fall back to the assembly version.
            return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
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
    /// Points the stereo-balance secondary audio session (ADR-0011) at the checked-in
    /// <c>tools\ffmpeg\ffmpeg.exe</c> when running from a repository checkout. Returns <c>null</c>
    /// when it cannot be found — the session then looks next to the app, otherwise stays unavailable
    /// (balance is simply a no-op) rather than failing at startup.
    /// </summary>
    private static PlaySphere.Audio.StereoBalanceAudioOptions? ConfigureStereoBalanceOptions()
    {
        var ffmpeg = FindUpwards("tools", "ffmpeg", "ffmpeg.exe");
        return ffmpeg is null ? null : new PlaySphere.Audio.StereoBalanceAudioOptions { ExecutablePath = ffmpeg };
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
            DiagnosticReportLocations.DefaultDataDirectory(),
            DiagnosticLogCatalog.LibVlcLogFileName);
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