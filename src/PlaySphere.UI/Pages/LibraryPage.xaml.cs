using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
// PlaySphere.Application (the project) is a reachable namespace here (this file uses its
// Library types below), which wins simple-name resolution for a bare "Application" identifier
// even over a same-named using-alias — namespace-member lookup across enclosing namespaces
// happens before using directives are consulted. Alias to a distinct name instead.
using XamlApplication = Microsoft.UI.Xaml.Application;
using PlaySphere.Application.Library;
using PlaySphere.Domain.Library;
using PlaySphere.UI.Helpers;

namespace PlaySphere.UI.Pages;

public sealed partial class LibraryPage : Page
{
    private readonly IMediaLibraryRepository? _repository = PlaySphereServices.GetOptional<IMediaLibraryRepository>();
    private readonly ILibraryScanner? _scanner = PlaySphereServices.GetOptional<ILibraryScanner>();

    public LibraryPage() => InitializeComponent();

    private async void Page_Loaded(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        if (_repository == null) return;

        var folders = await _repository.GetFoldersAsync();
        var items = await _repository.GetAllMediaItemsAsync();

        PopulateFolders(folders);
        PopulateMediaItems(items, "All Media");

        var hasContent = folders.Count > 0 || items.Count > 0;
        EmptyStateCard.Visibility = hasContent ? Visibility.Collapsed : Visibility.Visible;
        LibrarySearchBox.Visibility = hasContent ? Visibility.Visible : Visibility.Collapsed;
        LibrarySearchBox.Text = string.Empty;
    }

    private const char FolderGlyph = '\uE838'; // Segoe MDL2 "FolderOpen"

    private void PopulateFolders(IReadOnlyList<LibraryFolder> folders)
    {
        FoldersList.Children.Clear();
        FoldersSection.Visibility = folders.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var folder in folders)
        {
            var card = new Grid
            {
                CornerRadius = new CornerRadius(12),
                Background = (Microsoft.UI.Xaml.Media.Brush)XamlApplication.Current.Resources["BrushSurfaceCard"],
                Padding = new Thickness(14, 10, 14, 10),
                ColumnSpacing = 10,
            };
            card.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            card.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            card.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new FontIcon
            {
                Glyph = FolderGlyph.ToString(),
                FontSize = 18,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)XamlApplication.Current.Resources["BrushAccentBlue"],
            };
            Grid.SetColumn(icon, 0);
            card.Children.Add(icon);

            var texts = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(new TextBlock
            {
                Text = System.IO.Path.GetFileName(folder.Path.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : folder.Path,
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextTrimming = Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)XamlApplication.Current.Resources["BrushTextPrimary"],
            });
            texts.Children.Add(new TextBlock
            {
                Text = $"{folder.Path}  ·  Added {folder.AddedAt.ToLocalTime():d MMM yyyy}",
                FontSize = 11,
                TextTrimming = Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)XamlApplication.Current.Resources["BrushTextMuted"],
            });
            Grid.SetColumn(texts, 1);
            card.Children.Add(texts);

            var removeButton = new Button
            {
                Content = new FontIcon { Glyph = '\uE74D'.ToString(), FontSize = 14 },
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Background = (Microsoft.UI.Xaml.Media.Brush)XamlApplication.Current.Resources["BrushSurfaceHover"],
                Tag = folder.Id,
            };
            ToolTipService.SetToolTip(removeButton, "Remove folder");
            removeButton.Click += async (_, _) =>
            {
                if (_repository == null) return;
                await _repository.RemoveFolderAsync(folder.Id);
                await RefreshAsync();
            };
            Grid.SetColumn(removeButton, 2);
            card.Children.Add(removeButton);

            FoldersList.Children.Add(card);
        }
    }

    private void PopulateMediaItems(IReadOnlyList<LibraryMediaItem> items, string headerText)
    {
        MediaItemsList.Children.Clear();
        MediaItemsHeader.Text = headerText;
        MediaItemsSection.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var item in items)
        {
            var button = MediaCardBuilder.CreatePoster(
                item,
                width: 240,
                showProgress: false,
                item2 => Frame?.Navigate(typeof(PlayerPage), item2.FilePath));
            ToolTipService.SetToolTip(button, item.FilePath);
            MediaItemsList.Children.Add(button);
        }
    }

    private async void AddFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_repository == null || _scanner == null) return;

        try
        {
            var path = await FilePickerHelper.PickFolderAsync();
            if (string.IsNullOrEmpty(path)) return;

            var folder = await _repository.AddFolderAsync(path);

            ScanStatusPanel.Visibility = Visibility.Visible;
            ScanStatusText.Text = $"Scanning {folder.Path}…";
            try
            {
                var result = await _scanner.ScanFolderAsync(folder);
                ScanStatusText.Text = $"Scan complete: {result.Added} added, {result.Updated} updated" +
                    (result.Failed > 0 ? $", {result.Failed} failed" : string.Empty) + ".";
            }
            finally
            {
                await RefreshAsync();
            }
        }
        catch (InvalidOperationException)
        {
            // Folder already in the library — best-effort UX, no crash.
        }
    }

    private async void LibrarySearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_repository == null) return;

        var query = LibrarySearchBox.Text;
        if (string.IsNullOrWhiteSpace(query))
        {
            PopulateMediaItems(await _repository.GetAllMediaItemsAsync(), "All Media");
            return;
        }

        PopulateMediaItems(await _repository.SearchAsync(query), $"Results for \"{query}\"");
    }
}
