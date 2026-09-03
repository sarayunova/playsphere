using System.Runtime.InteropServices;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace PlaySphere.UI.Helpers;

/// <summary>
/// Helper for opening media files with the Windows native file picker.
/// Handles HWND initialization required for unpackaged WinUI 3 apps.
/// </summary>
public static class FilePickerHelper
{
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetActiveWindow();

    /// <summary>
    /// Prompts the user to pick a media file.
    /// Returns the absolute file path, or null if cancelled.
    /// </summary>
    public static async Task<string?> PickMediaFileAsync(IntPtr customHwnd = default)
    {
        var picker = new FileOpenPicker();

        picker.FileTypeFilter.Add(".mp4");
        picker.FileTypeFilter.Add(".mkv");
        picker.FileTypeFilter.Add(".mp3");
        picker.FileTypeFilter.Add(".flac");
        picker.FileTypeFilter.Add(".wav");
        picker.FileTypeFilter.Add(".avi");
        picker.FileTypeFilter.Add(".mov");
        picker.FileTypeFilter.Add(".webm");
        picker.FileTypeFilter.Add(".m4a");
        picker.FileTypeFilter.Add(".aac");
        picker.FileTypeFilter.Add(".ogg");
        picker.FileTypeFilter.Add(".ts");
        picker.FileTypeFilter.Add("*");

        var hwnd = customHwnd != IntPtr.Zero ? customHwnd : GetActiveWindow();
        if (hwnd != IntPtr.Zero)
        {
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        StorageFile? file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    /// <summary>
    /// Prompts the user to pick an external subtitle file (.srt, .ass, .ssa, .vtt, .sub, etc.).
    /// Returns the absolute file path, or null if cancelled.
    /// </summary>
    public static async Task<string?> PickSubtitleFileAsync(IntPtr customHwnd = default)
    {
        var picker = new FileOpenPicker();

        picker.FileTypeFilter.Add(".srt");
        picker.FileTypeFilter.Add(".ass");
        picker.FileTypeFilter.Add(".ssa");
        picker.FileTypeFilter.Add(".vtt");
        picker.FileTypeFilter.Add(".sub");
        picker.FileTypeFilter.Add(".smi");
        picker.FileTypeFilter.Add(".idx");
        picker.FileTypeFilter.Add(".txt");
        picker.FileTypeFilter.Add("*");

        var hwnd = customHwnd != IntPtr.Zero ? customHwnd : GetActiveWindow();
        if (hwnd != IntPtr.Zero)
        {
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        StorageFile? file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    /// <summary>
    /// Prompts the user for a destination path to export a subtitle track as SRT.
    /// Returns the absolute file path, or null if cancelled.
    /// </summary>
    public static async Task<string?> PickSaveSubtitleFileAsync(string suggestedFileName, IntPtr customHwnd = default)
    {
        var picker = new FileSavePicker();
        picker.FileTypeChoices.Add("SubRip Subtitle", [".srt"]);
        picker.SuggestedFileName = suggestedFileName;

        var hwnd = customHwnd != IntPtr.Zero ? customHwnd : GetActiveWindow();
        if (hwnd != IntPtr.Zero)
        {
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        StorageFile? file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    /// <summary>
    /// Prompts the user to explicitly pick a folder to add to the library (PICKLE.md §22 — the
    /// library never scans drives on its own; every folder is added by this kind of explicit
    /// user action). Returns the absolute folder path, or null if cancelled.
    /// </summary>
    public static async Task<string?> PickFolderAsync(IntPtr customHwnd = default)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");

        var hwnd = customHwnd != IntPtr.Zero ? customHwnd : GetActiveWindow();
        if (hwnd != IntPtr.Zero)
        {
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        StorageFolder? folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    /// <summary>
    /// Prompts the user for a destination path to save a video snapshot as PNG. Returns the
    /// absolute file path, or null if cancelled.
    /// </summary>
    public static async Task<string?> PickSaveSnapshotFileAsync(string suggestedFileName, IntPtr customHwnd = default)
    {
        var picker = new FileSavePicker();
        picker.FileTypeChoices.Add("PNG Image", [".png"]);
        picker.SuggestedFileName = suggestedFileName;

        var hwnd = customHwnd != IntPtr.Zero ? customHwnd : GetActiveWindow();
        if (hwnd != IntPtr.Zero)
        {
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        StorageFile? file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    /// <summary>
    /// Prompts the user for a destination path to save an exported clip (Phase 7 Mark In/Out).
    /// Returns the absolute file path, or null if cancelled.
    /// </summary>
    public static async Task<string?> PickSaveClipFileAsync(string suggestedFileName, IntPtr customHwnd = default)
    {
        var picker = new FileSavePicker();
        picker.FileTypeChoices.Add("Video (MP4)", [".mp4"]);
        picker.FileTypeChoices.Add("Matroska (MKV)", [".mkv"]);
        picker.FileTypeChoices.Add("QuickTime (MOV)", [".mov"]);
        picker.SuggestedFileName = suggestedFileName;

        var hwnd = customHwnd != IntPtr.Zero ? customHwnd : GetActiveWindow();
        if (hwnd != IntPtr.Zero)
        {
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        StorageFile? file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    /// <summary>
    /// Prompts the user for a destination path to save a converted media file (Phase 7 conversion).
    /// The single <paramref name="extension"/>/display-name pair is taken from the chosen
    /// <see cref="PlaySphere.Domain.Media.ConversionPreset"/>. Returns the absolute file path, or
    /// null if cancelled.
    /// </summary>
    public static async Task<string?> PickSaveConvertFileAsync(string suggestedFileName, string displayName, string extension, IntPtr customHwnd = default)
    {
        var picker = new FileSavePicker();
        picker.FileTypeChoices.Add(displayName, [extension]);
        picker.SuggestedFileName = suggestedFileName;

        var hwnd = customHwnd != IntPtr.Zero ? customHwnd : GetActiveWindow();
        if (hwnd != IntPtr.Zero)
        {
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        StorageFile? file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    /// <summary>
    /// Prompts the user to pick an M3U/M3U8 playlist file to import. Returns the absolute file
    /// path, or null if cancelled.
    /// </summary>
    public static async Task<string?> PickM3uFileAsync(IntPtr customHwnd = default)
    {
        var picker = new FileOpenPicker();

        picker.FileTypeFilter.Add(".m3u");
        picker.FileTypeFilter.Add(".m3u8");
        picker.FileTypeFilter.Add("*");

        var hwnd = customHwnd != IntPtr.Zero ? customHwnd : GetActiveWindow();
        if (hwnd != IntPtr.Zero)
        {
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        StorageFile? file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    /// <summary>
    /// Prompts the user for a destination path to export a playlist as M3U. Returns the absolute
    /// file path, or null if cancelled.
    /// </summary>
    public static async Task<string?> PickSaveM3uFileAsync(string suggestedFileName, IntPtr customHwnd = default)
    {
        var picker = new FileSavePicker();
        picker.FileTypeChoices.Add("M3U Playlist", [".m3u"]);
        picker.SuggestedFileName = suggestedFileName;

        var hwnd = customHwnd != IntPtr.Zero ? customHwnd : GetActiveWindow();
        if (hwnd != IntPtr.Zero)
        {
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        StorageFile? file = await picker.PickSaveFileAsync();
        return file?.Path;
    }
}
