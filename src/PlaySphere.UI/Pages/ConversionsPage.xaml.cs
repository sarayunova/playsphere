using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PlaySphere.Application.Media;
using PlaySphere.Domain.Media;
using PlaySphere.UI.Services;

namespace PlaySphere.UI.Pages;

/// <summary>View row for one background conversion job (PICKLE.md §30). Plain settable properties so
/// the XAML type-info provider can construct this DataTemplate row type (x:DataType).</summary>
public sealed partial class ConversionJobRow
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string InputPath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public string StatusText { get; set; } = string.Empty;
    public Brush StatusBrush { get; set; } = new SolidColorBrush(Microsoft.UI.Colors.Gray);
    public double ProgressPercent { get; set; }
    public bool ShowProgress { get; set; }
    public bool ShowCancel { get; set; }
    public bool ShowRetry { get; set; }
    public bool ShowOpenFolder { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}

/// <summary>
/// Live listing of background conversion jobs: progress, cancel, retry and open-output-folder
/// (PICKLE.md §30). Resolves the queue via DI defensively — when it is absent the page degrades to
/// an empty state rather than crashing.
/// </summary>
public sealed partial class ConversionsPage : Page
{
    private readonly ObservableCollection<ConversionJobRow> _rows = [];
    private IConversionQueue? _queue;

    public ObservableCollection<ConversionJobRow> Jobs => _rows;

    public ConversionsPage()
    {
        InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _queue = PlaySphereServices.GetOptional<IConversionQueue>();
        if (_queue is not null)
        {
            _queue.JobsChanged += Queue_JobsChanged;
            Queue_JobsChanged(this, EventArgs.Empty);
        }
        else
        {
            EmptyStateText.Text = "Background conversion is not available in this installation.";
            EmptyStateText.Visibility = Visibility.Visible;
        }
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_queue is not null)
        {
            _queue.JobsChanged -= Queue_JobsChanged;
            _queue = null;
        }
    }

    private void Queue_JobsChanged(object? sender, EventArgs e)
    {
        var queue = _queue;
        if (queue is null)
        {
            return;
        }

        var snapshot = queue.Jobs;
        if (DispatcherQueue is null)
        {
            Refresh(snapshot);
            return;
        }

        DispatcherQueue.TryEnqueue(() => Refresh(snapshot));
    }

    private void Refresh(IReadOnlyList<ConversionJob> snapshot)
    {
        _rows.Clear();
        var accent = ResourceBrush("BrushAccentBlue") ?? new SolidColorBrush(Microsoft.UI.Colors.DodgerBlue);
        var warn = ResourceBrush("BrushWarning") ?? new SolidColorBrush(Microsoft.UI.Colors.Goldenrod);
        var ok = ResourceBrush("BrushSuccess") ?? new SolidColorBrush(Microsoft.UI.Colors.LimeGreen);
        var err = ResourceBrush("BrushError") ?? new SolidColorBrush(Microsoft.UI.Colors.IndianRed);
        var idle = ResourceBrush("BrushTextSecondary") ?? new SolidColorBrush(Microsoft.UI.Colors.Gray);

        foreach (var job in snapshot)
        {
            var (statusText, brush) = job.Status switch
            {
                ConversionJobStatus.Queued => ("Queued", idle),
                ConversionJobStatus.Running => ("Running", accent),
                ConversionJobStatus.Succeeded => ("Done", ok),
                ConversionJobStatus.Failed => ("Failed", err),
                ConversionJobStatus.Cancelled => ("Cancelled", idle),
                _ => (job.Status.ToString(), idle),
            };

            _rows.Add(new ConversionJobRow
            {
                Id = job.Id,
                Label = job.Label,
                InputPath = job.InputPath,
                OutputPath = job.OutputPath,
                StatusText = statusText,
                StatusBrush = brush,
                ProgressPercent = Math.Clamp(job.ProgressFraction * 100, 0, 100),
                ShowProgress = job.Status is ConversionJobStatus.Queued or ConversionJobStatus.Running,
                ShowCancel = job.IsActive,
                ShowRetry = job.CanRetry,
                ShowOpenFolder = job.CanOpenOutputFolder,
                ErrorMessage = job.Status == ConversionJobStatus.Failed ? job.ErrorMessage ?? string.Empty : string.Empty,
            });
        }

        EmptyStateText.Visibility = snapshot.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_queue is not null && sender is Button { Tag: string id })
        {
            _queue.Cancel(id);
        }
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_queue is not null && sender is Button { Tag: string id })
        {
            _queue.Retry(id);
        }
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string path })
        {
            ExplorerHelper.OpenContainingFolder(path);
        }
    }

    private static Brush? ResourceBrush(string key)
    {
        try
        {
            return Microsoft.UI.Xaml.Application.Current?.Resources[key] as Brush;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>Opens Windows Explorer at a given path (surfacing the output of a finished job).</summary>
internal static class ExplorerHelper
{
    public static void OpenContainingFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var ps = new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
            {
                UseShellExecute = false,
            };
            Process.Start(ps);
        }
        catch
        {
            // Best effort — never take the UI down launching Explorer.
        }
    }
}