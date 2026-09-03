using System.IO;
using LibVLCSharp.Shared;
using LibVLCLib = LibVLCSharp.Shared.LibVLC;

namespace PlaySphere.Playback.LibVLC;

/// <summary>
/// Owns libVLC native lifecycle: one-time Core initialization (thread-safe) and LibVLC
/// instance creation from <see cref="LibVlcOptions"/>. The rest of the app only ever
/// sees LibVLC instances through <see cref="PlaySphere.Playback.Abstractions"/>.
/// </summary>
public static class LibVlcBootstrap
{
    private static readonly object SyncRoot = new();
    private static bool _nativeInitialized;

    // Bounded in-memory tail of the most recent libVLC log lines (thread-safe), so a later
    // Play/EncounteredError failure can surface the deep libVLC reason on-screen instead of only
    // writing it to a file the user has to go find.
    private const int MaxBufferedLines = 256;
    private static readonly List<string> RecentLines = [];
    private static readonly object LogSync = new();

    /// <summary>
    /// Runs LibVLCSharp's one-time native search/init. Safe to call concurrently; no-op
    /// after the first success. Falls back to a recursive search of the app base directory
    /// when the default probe cannot locate libVLC (e.g. unusual output layouts).
    /// </summary>
    /// <exception cref="VLCException">Native libVLC could not be located/loaded.</exception>
    public static void EnsureNativeInitialized()
    {
        if (_nativeInitialized)
        {
            return;
        }

        lock (SyncRoot)
        {
            if (_nativeInitialized)
            {
                return;
            }

            try
            {
                Core.Initialize();
            }
            catch (VLCException)
            {
                var nativeDir = FindNativeDirectory(AppContext.BaseDirectory);
                if (nativeDir is null)
                {
                    throw;
                }

                Core.Initialize(nativeDir);
            }

            _nativeInitialized = true;
        }
    }

    public static LibVLCLib CreateLibVlc(LibVlcOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        EnsureNativeInitialized();

        var args = new List<string>(options.ExtraArguments)
        {
            "--no-video-title-show",
            "--no-osd",
            $"--verbose={Math.Clamp(options.Verbosity, 0, 2)}",
        };

        if (options.DisableAudio)
        {
            args.Add("--no-audio");
        }

        var libVlc = new LibVLCLib(args.ToArray());

        // Wire libVLC's native message log to a file. This MUST happen at/right after LibVLC
        // creation: LibVLCSharp only installs the native log callback when the Log event has a
        // subscriber at this point, so a later subscription (e.g. from the engine ctor) never fires.
        if (!string.IsNullOrWhiteSpace(options.LogFilePath))
        {
            var filePath = options.LogFilePath;
            libVlc.Log += (_, e) => AppendLog(filePath, e.FormattedLog ?? $"{e.Level}: {e.Message} [{e.Module}]");
        }
        else
        {
            // Even without a file, buffer the tail so Play-failure diagnostics have context.
            libVlc.Log += (_, e) => BufferLog(e.FormattedLog ?? $"{e.Level}: {e.Message} [{e.Module}]");
        }

        return libVlc;
    }

    /// <summary>Returns a snapshot (newest-last) of the most recent libVLC log lines since startup.</summary>
    public static IReadOnlyList<string> GetRecentLogLines()
    {
        lock (LogSync)
        {
            return RecentLines.ToArray();
        }
    }

    private static void BufferLog(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        lock (LogSync)
        {
            RecentLines.Add(line);
            if (RecentLines.Count > MaxBufferedLines)
            {
                RecentLines.RemoveRange(0, RecentLines.Count - MaxBufferedLines);
            }
        }
    }

    private static void AppendLog(string path, string line)
    {
        BufferLog(line);
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"[{DateTime.UtcNow:o}] {line}\n");
        }
        catch
        {
            // Logging failures must never take the player down.
        }
    }

    private static string? FindNativeDirectory(string startDirectory)
    {
        foreach (var candidate in Directory.EnumerateDirectories(startDirectory, "*", SearchOption.AllDirectories))
        {
            if (File.Exists(Path.Combine(candidate, "libvlc.dll")) &&
                File.Exists(Path.Combine(candidate, "libvlccore.dll")))
            {
                return candidate;
            }
        }

        return null;
    }
}