using System;
using System.IO;
using System.Text;
using System.Threading;
using PlaySphere.Application.Diagnostics;

namespace PlaySphere.Audio;

/// <summary>
/// Lightweight, best-effort append-only trace for diagnosing the visualizer audio-capture path
/// (ADR-0009) on machines where the visualizer appears static. Writes to
/// <c>%LOCALAPPDATA%\LiVi PlaySphere Studio\visualizer-audio.log</c> so a reproduce produces an
/// evidence file the developer/user can share. Never throws, never blocks the audio thread
/// (only every Nth call touches disk, and a failed write is swallowed). Disabled when logging off.
/// </summary>
public static class VisualizerAudioTrace
{
    private const int LogWhenCallIndexMultiple = 50;
    private static readonly object Gate = new();
    private static string? _logPath;
    private static bool _enabled = true;

    /// <summary>
    /// Enables/disables file tracing. Default on. Exposed for tests to silence noisy logs.
    /// </summary>
    public static bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    /// <summary>The current log file path (null until first write).</summary>
    public static string? LogPath => _logPath;

    /// <summary>
    /// Appends a timestamped line. The write is debounced (normally only every Nth call is actually
    /// written) to avoid saturating the disk on chatty capture threads; <paramref name="force"/>
    /// bypasses the debounce for important lifecycle events.
    /// </summary>
    public static void Log(string message, bool force = false)
    {
        if (!_enabled)
        {
            return;
        }

        lock (Gate)
        {
            if (!force && Interlocked.Increment(ref _callIndex) % LogWhenCallIndexMultiple != 0)
            {
                return;
            }

            try
            {
                _logPath ??= BuildDefaultPath();
                var dir = Path.GetDirectoryName(_logPath);
                if (dir != null && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var line = $"{DateTime.UtcNow:u} {message}{Environment.NewLine}";
                File.AppendAllText(_logPath, line);
            }
            catch
            {
                // A diagnostic write must never break the player.
            }
        }
    }

    private static int _callIndex;

    private static string BuildDefaultPath()
        => Path.Combine(
            DiagnosticReportLocations.DefaultDataDirectory(),
            DiagnosticLogCatalog.VisualizerAudioLogFileName);
}