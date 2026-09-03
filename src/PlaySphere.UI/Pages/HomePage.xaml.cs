using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
// PlaySphere.Application (the project) is a reachable namespace here (this file uses its
// Library types below), which wins simple-name resolution for a bare "Application" identifier
// even over a same-named using-alias — namespace-member lookup across enclosing namespaces
// happens before using directives are consulted. Alias to a distinct name instead.
using XamlApplication = Microsoft.UI.Xaml.Application;
using PlaySphere.Application.Library;
using PlaySphere.Domain.Library;
using PlaySphere.UI.Helpers;
using PlaySphere.UI.Navigation;

namespace PlaySphere.UI.Pages;

public sealed partial class HomePage : Page
{
    private readonly IMediaLibraryRepository? _repository = PlaySphereServices.GetOptional<IMediaLibraryRepository>();

    public HomePage() => InitializeComponent();

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (_repository == null) return;

        PopulateRow(ContinueWatchingRow, await _repository.GetContinueWatchingAsync(maxCount: 6),
            "Nothing here yet. Your in-progress media will appear here.");
        PopulateRow(RecentlyPlayedRow, await _repository.GetRecentlyPlayedAsync(maxCount: 6),
            "Nothing played yet. Recent media will appear here.");
    }

    private void PopulateRow(StackPanel row, IReadOnlyList<LibraryMediaItem> items, string emptyText)
    {
        row.Children.Clear();

        if (items.Count == 0)
        {
            row.Children.Add(new Border
            {
                Style = (Style)XamlApplication.Current.Resources["EmptyStateCardStyle"],
                Width = 260,
                Child = new TextBlock
                {
                    Style = (Style)XamlApplication.Current.Resources["MutedTextStyle"],
                    Text = emptyText,
                    TextWrapping = TextWrapping.Wrap,
                },
            });
            return;
        }

        foreach (var item in items)
        {
            row.Children.Add(MediaCardBuilder.CreatePoster(
                item,
                width: 260,
                showProgress: item.WatchedPercentage > 0,
                item2 => Frame?.Navigate(typeof(PlayerPage), item2.FilePath)));
        }
    }

    private async void OpenFileButton_Click(object sender, RoutedEventArgs e)
    {
        var filePath = await FilePickerHelper.PickMediaFileAsync();
        if (!string.IsNullOrEmpty(filePath) && Frame != null)
        {
            Frame.Navigate(typeof(PlayerPage), filePath);
        }
    }

    private async void OpenUrlButton_Click(object sender, RoutedEventArgs e)
    {
        if (Frame == null || XamlRoot is null) return;

        var input = new TextBox
        {
            Header = "Stream URL",
            PlaceholderText = "https://example.com/live.ts  ·  rtmp:// ...  ·  rtsp:// ...",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var dialog = new ContentDialog
        {
            Title = "Open Streaming URL",
            PrimaryButtonText = "Play",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            Content = new StackPanel
            {
                Spacing = 8,
                MinWidth = 360,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Stream DRM-free audio/video (HLS, DASH, RTSP, livestreams, IPTV, podcasts, your own media server). Subscription/DRM services like Netflix are not supported.",
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = (Brush)XamlApplication.Current.Resources["BrushTextMuted"],
                    },
                    input,
                },
            },
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        var url = input.Text?.Trim();
        if (string.IsNullOrEmpty(url))
        {
            return;
        }

        try
        {
            var media = PlaySphere.Domain.Media.MediaFile.FromPathOrUri(url);
            if (media.IsRemoteUri)
            {
                Frame.Navigate(typeof(PlayerPage), media.FilePath);
                return;
            }
        }
        catch (ArgumentException)
        {
            // Fall through to the generic error dialog below (invalid/unsupported scheme).
        }

        await new ContentDialog
        {
            Title = "Unable to Open URL",
            Content = "That doesn't look like a streaming URL. Paste an http(s), rtmp, rtsp, mms, udp, rtp or ftp address.",
            CloseButtonText = "OK",
            XamlRoot = XamlRoot,
        }.ShowAsync();
    }
}
