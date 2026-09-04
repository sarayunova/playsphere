using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PlaySphere.Application.AI;
using PlaySphere.Application.Library;
using PlaySphere.Domain.AI;
using PlaySphere.Domain.Library;
using PlaySphere.UI.Navigation;

namespace PlaySphere.UI.Pages;

public sealed partial class AiMediaPage : Page
{
    private readonly IMediaLibraryRepository? _repository;
    private readonly ITranscribeMediaRunner? _runner;
    private readonly ISummarizeMediaRunner? _summaryRunner;
    private readonly IAskMediaRunner? _askRunner;
    private readonly IAiCredentialStore? _store;

    private readonly List<LibraryMediaItem> _catalog = new();
    private AiTranscript? _persisted;
    private IReadOnlyList<AiTranscriptSegment> _allSegments = [];
    private string _mediaPath = string.Empty;
    private int _mediaItemId;
    private bool _consented;
    private bool _busy;
    private bool _summarizerBusy;
    private bool _askBusy;

    public AiMediaPage()
    {
        InitializeComponent();

        _repository = PlaySphereServices.GetOptional<IMediaLibraryRepository>();
        _runner = PlaySphereServices.GetOptional<ITranscribeMediaRunner>();
        _summaryRunner = PlaySphereServices.GetOptional<ISummarizeMediaRunner>();
        _askRunner = PlaySphereServices.GetOptional<IAskMediaRunner>();
        _store = PlaySphereServices.GetOptional<IAiCredentialStore>();

        if (_repository is null || _runner is null || _store is null)
        {
            MediaHeaderText.Text = "AI Media is unavailable";
            TranscribeStatusText.Text =
                "This feature could not be started (missing services). Restart the app and try again.";
        }

        if (_summaryRunner is null)
        {
            GenerateSummaryButton.IsEnabled = false;
            SummaryStatusText.Text = "Summary is unavailable (missing service).";
        }

        if (_askRunner is null)
        {
            AskButton.IsEnabled = false;
            AskBox.IsEnabled = false;
            AskStatusText.Text = "Ask is unavailable (missing service).";
        }

        Loaded += AiMediaPage_Loaded;
    }

    private async void AiMediaPage_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateApiKeyUi();
        await LoadCatalogAsync();
    }

    private void UpdateApiKeyUi()
    {
        if (_store is null)
        {
            ApiKeyStatusText.Text = "Credential store unavailable.";
            ApiKeyButton.IsEnabled = false;
            return;
        }

        var has = _store.HasApiKey();
        ApiKeyStatusText.Text = has
            ? "An OpenAI API key is configured. Your key stays in Windows Credential Manager and is used only for the actions you start here."
            : "No API key set. AI actions need an OpenAI API key.";
        ApiKeyButton.Content = has ? "Clear key" : "Set key";
    }

    private async Task LoadCatalogAsync()
    {
        if (_repository is null)
        {
            return;
        }

        try
        {
            var items = await _repository.GetAllMediaItemsAsync();
            _catalog.Clear();
            _catalog.AddRange(items);
            UpdateCatalogComboBox();
        }
        catch
        {
            TranscribeStatusText.Text = "Could not load your media library.";
        }
    }

    private void UpdateCatalogComboBox()
    {
        MediaComboBox.Items.Clear();
        foreach (var item in _catalog.OrderBy(i => DisplayName(i)))
        {
            var combo = new ComboBoxItem
            {
                Content = DisplayName(item),
                Tag = item,
            };
            MediaComboBox.Items.Add(combo);
        }

        MediaComboBox.IsEnabled = _catalog.Count > 0;
        TranscribeButton.IsEnabled = MediaComboBox.SelectedItem is not null;
    }

    private static string DisplayName(LibraryMediaItem item) =>
        string.IsNullOrWhiteSpace(item.Title) ? item.FilePath : item.Title!;

    private void MediaComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = Selected;
        TranscribeButton.IsEnabled = selected is not null && !_busy;
        UpdateAiActionEnablement();

        if (selected is null)
        {
            _mediaPath = string.Empty;
            _mediaItemId = 0;
            _persisted = null;
            _allSegments = [];
            SummaryTextBlock.Visibility = Visibility.Collapsed;
            SummaryStatusText.Text = "Summarize this file from its transcript (4. summary).";
            AnswerList.ItemsSource = null;
            AskStatusText.Text = "Ask anything about the content. Answers with a timestamp click to jump to that moment (5. ask).";
            RenderTranscript();
            return;
        }

        _mediaPath = selected.FilePath;
        _mediaItemId = selected.Id;
        _ = LoadPersistedTranscriptAsync();
        _ = LoadPersistedSummaryAsync();
    }

    private LibraryMediaItem? Selected =>
        (MediaComboBox.SelectedItem as ComboBoxItem)?.Tag as LibraryMediaItem;

    private async Task LoadPersistedTranscriptAsync()
    {
        if (_repository is null || _mediaItemId == 0)
        {
            return;
        }

        try
        {
            _persisted = await _repository.GetTranscriptAsync(_mediaItemId);
        }
        catch
        {
            _persisted = null;
        }

        _allSegments = _persisted?.Segments ?? [];
        SearchBox.Text = string.Empty;
        UpdateAiActionEnablement();
        RenderTranscript();
    }

    private void UpdateAiActionEnablement()
    {
        var available = !_busy && !_summarizerBusy && !_askBusy && Selected is not null;
        GenerateSummaryButton.IsEnabled = available;
        AskButton.IsEnabled = available;
        AskBox.IsEnabled = available;
    }

    private async void ApiKeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_store is null)
        {
            return;
        }

        if (_store.HasApiKey())
        {
            _store.ClearApiKey();
            UpdateApiKeyUi();
            return;
        }

        var box = new PasswordBox { PlaceholderText = "sk-…" };
        var dialog = new ContentDialog
        {
            Title = "Set OpenAI API key",
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Your key is stored only in Windows Credential Manager and is sent only to OpenAI when you start an action.",
                        TextWrapping = TextWrapping.Wrap,
                    },
                    box,
                },
            },
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        ContentDialogResult result;
        try
        {
            result = await dialog.ShowAsync();
        }
        catch (Exception)
        {
            // WinUI allows only one ContentDialog open per XamlRoot at a time; a second
            // ShowAsync() while one is still closing throws and would otherwise crash this
            // async void handler. Treat as cancelled rather than crash.
            return;
        }
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        _store.SetApiKey(box.Password);
        UpdateApiKeyUi();
    }

    private async void TranscribeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_runner is null || Selected is null)
        {
            return;
        }

        if (!_store?.HasApiKey() ?? true)
        {
            TranscribeStatusText.Text = "Set an OpenAI API key first (above) to transcribe.";
            return;
        }

        if (!_consented)
        {
            if (!await ConfirmConsentAsync())
            {
                return;
            }
        }

        _busy = true;
        TranscribeButton.IsEnabled = false;
        TranscribeStatusText.Text = "Extracting audio and transcribing… this can take a minute.";
        TranscribeButton.Content = "Transcribing…";

        try
        {
            var result = await _runner.RunAsync(_mediaPath);
            if (result.Success)
            {
                _consented = true;
                TranscribeStatusText.Text = result.SegmentCount > 0
                    ? $"Transcription complete — {result.SegmentCount} timed segments. You can search below."
                    : "Transcription complete.";
                await LoadPersistedTranscriptAsync();
            }
            else
            {
                TranscribeStatusText.Text = result.ErrorMessage ?? "Transcription failed.";
            }
        }
        catch (Exception)
        {
            TranscribeStatusText.Text = "Transcription failed. Try again.";
        }
        finally
        {
            _busy = false;
            TranscribeButton.IsEnabled = Selected is not null;
            TranscribeButton.Content = "2. Transcribe";
        }
    }

    private async Task<bool> ConfirmConsentAsync()
    {
        var dialog = new ContentDialog
        {
            Title = "Transcribe this media?",
            Content = new TextBlock
            {
                Text = "AI transcription extracts the audio and uploads it to your configured AI provider. " +
                       "The picture is never uploaded. Do you want to continue?",
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = "Transcribe",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        try
        {
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        catch (Exception)
        {
            // Another ContentDialog was already open (WinUI allows only one per XamlRoot);
            // treat as declined rather than crash.
            return false;
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        RenderTranscript();
    }

    private void RenderTranscript()
    {
        var query = SearchBox.Text;
        IReadOnlyList<AiTranscriptSegment> rows;

        if (string.IsNullOrWhiteSpace(query))
        {
            rows = _allSegments;
            TranscriptSummaryText.Text = _allSegments.Count == 0
                ? "No transcript yet. Pick a file and transcribe it, or it may already be saved from a previous run."
                : $"{_allSegments.Count} segments · use the search box to jump to a spoken moment.";
        }
        else
        {
            rows = TranscriptSearcher.Search(_allSegments, query);
            TranscriptSummaryText.Text = rows.Count == 0
                ? $"No matches for \u201c{query.Trim()}\u201d."
                : $"{rows.Count} match(es) for \u201c{query.Trim()}\u201d. Click one to jump to that moment.";
        }

        TranscriptList.ItemsSource = rows
            .Select(s => new TranscriptRow(FormatTimestamp(s.StartSeconds, s.EndSeconds), s.Text, s.StartSeconds))
            .ToList();
    }

    private static string FormatTimestamp(double start, double end)
    {
        var a = TimeSpan.FromSeconds(Math.Max(0, start));
        var b = TimeSpan.FromSeconds(Math.Max(0, end));
        static string F(TimeSpan t) => t.TotalHours >= 1
            ? $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes:00}:{t.Seconds:00}";
        return $"{F(a)} - {F(b)}";
    }

    private static string FormatStamp(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes:00}:{t.Seconds:00}";
    }

    private void TranscriptItem_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is TranscriptRow row)
        {
            NavigateToMedia(row.StartSeconds);
        }
    }

    private async Task LoadPersistedSummaryAsync()
    {
        if (_repository is null || _mediaItemId == 0)
        {
            return;
        }

        AiSummary? summary = null;
        try
        {
            var artifacts = await _repository.GetArtifactsAsync(_mediaItemId);
            var match = artifacts.LastOrDefault(a => a.Key == ISummarizeMediaRunner.SummaryArtifactKey);
            if (match is not null)
            {
                summary = System.Text.Json.JsonSerializer.Deserialize<AiSummary>(match.PayloadJson);
            }
        }
        catch
        {
            summary = null;
        }

        if (summary is not null && !string.IsNullOrWhiteSpace(summary.Text))
        {
            ShowSummary(summary);
        }
        else
        {
            SummaryTextBlock.Visibility = Visibility.Collapsed;
            SummaryStatusText.Text = "No summary yet. Generate one from this file's transcript (4. summary).";
        }

        UpdateAiActionEnablement();
    }

    private void ShowSummary(AiSummary summary)
    {
        SummaryTextBlock.Text = summary.Text;
        SummaryTextBlock.Visibility = Visibility.Visible;
        var words = Math.Max(0, summary.EstimatedWordCount);
        SummaryStatusText.Text = words > 0
            ? $"Summary ready — about {words} word(s). Generate again to refresh."
            : "Summary ready. Generate again to refresh.";
    }

    private async void GenerateSummaryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_summaryRunner is null || Selected is null)
        {
            return;
        }

        if (ConfirmProviderNeeded() is false)
        {
            return;
        }

        _summarizerBusy = true;
        GenerateSummaryButton.IsEnabled = false;
        UpdateAiActionEnablement();
        SummaryStatusText.Text = "Summarizing…";

        try
        {
            var result = await _summaryRunner.SummarizeAsync(_mediaPath);
            if (result.Success && result.Summary is not null)
            {
                ShowSummary(result.Summary);
            }
            else
            {
                SummaryTextBlock.Visibility = Visibility.Collapsed;
                SummaryStatusText.Text = result.ErrorMessage ?? "Could not generate a summary. Try again.";
            }
        }
        catch (Exception)
        {
            SummaryTextBlock.Visibility = Visibility.Collapsed;
            SummaryStatusText.Text = "Could not generate a summary. Try again.";
        }
        finally
        {
            _summarizerBusy = false;
            UpdateAiActionEnablement();
        }
    }

    private async void AskButton_Click(object sender, RoutedEventArgs e)
    {
        await RunAskAsync(AskBox.Text);
    }

    private async void AskBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter && AskButton.IsEnabled)
        {
            await RunAskAsync(AskBox.Text);
        }
    }

    private async Task RunAskAsync(string question)
    {
        question = (question ?? string.Empty).Trim();
        if (_askRunner is null || Selected is null || question.Length == 0)
        {
            return;
        }

        if (ConfirmProviderNeeded() is false)
        {
            return;
        }

        _askBusy = true;
        AskButton.IsEnabled = false;
        UpdateAiActionEnablement();
        AskStatusText.Text = "Asking…";

        try
        {
            var result = await _askRunner.AskAsync(_mediaPath, question);
            if (result.Success && result.Answers is { Count: > 0 })
            {
                AnswerList.ItemsSource = result.Answers
                    .Select(a => new AnswerRow(
                        a.StartSeconds.HasValue ? FormatStamp(a.StartSeconds.Value) : string.Empty,
                        a.Text,
                        a.StartSeconds,
                        a.StartSeconds.HasValue ? Visibility.Visible : Visibility.Collapsed))
                    .ToList();
                AskStatusText.Text = $"{result.Answers.Count} answer(s). Click a timestamp to jump to that moment.";
            }
            else
            {
                AnswerList.ItemsSource = null;
                AskStatusText.Text = result.ErrorMessage ?? "The provider returned no answers. Try rephrasing.";
            }
        }
        catch (Exception)
        {
            AnswerList.ItemsSource = null;
            AskStatusText.Text = "Could not get an answer. Try again.";
        }
        finally
        {
            _askBusy = false;
            UpdateAiActionEnablement();
            AskButton.IsEnabled = true;
        }
    }

    private bool? ConfirmProviderNeeded()
    {
        if (_store?.HasApiKey() ?? false)
        {
            return true;
        }

        _ = ConfirmProviderDialogAsync();
        return false;
    }

    private async Task ConfirmProviderDialogAsync()
    {
        if (_store is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Set an OpenAI API key first",
            Content = new TextBlock
            {
                Text = "AI actions need an OpenAI API key. Your key stays in Windows Credential Manager and is only used for the actions you start here.",
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = "Set key",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        try
        {
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            var box = new PasswordBox { PlaceholderText = "sk-…" };
            var setDialog = new ContentDialog
            {
                Title = "Set OpenAI API key",
                Content = box,
                PrimaryButtonText = "Save",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
            };

            if (await setDialog.ShowAsync() == ContentDialogResult.Primary)
            {
                _store.SetApiKey(box.Password);
                UpdateApiKeyUi();
            }
        }
        catch (Exception)
        {
            // Another ContentDialog was already open (WinUI allows only one per XamlRoot);
            // this call is fire-and-forget (see ConfirmProviderNeeded), so swallow rather than
            // leave an unobserved faulted task.
        }
    }

    private void AnswerItem_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is AnswerRow row && row.StartSeconds.HasValue)
        {
            NavigateToMedia(row.StartSeconds.Value);
        }
    }

    private void NavigateToMedia(double startSeconds)
    {
        if (string.IsNullOrEmpty(_mediaPath))
        {
            return;
        }

        var payload = new PlayerQueuePayload(
            new[] { _mediaPath },
            startFilePath: _mediaPath,
            position: TimeSpan.FromSeconds(startSeconds));
        Frame?.Navigate(typeof(PlayerPage), payload);
    }

    private sealed record TranscriptRow(string Timestamp, string Text, double StartSeconds);

    private sealed record AnswerRow(string Timestamp, string Text, double? StartSeconds, Visibility TimestampVisibility);
}
