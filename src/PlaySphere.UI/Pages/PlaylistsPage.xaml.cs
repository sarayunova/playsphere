using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
// PlaySphere.Application is a reachable namespace here (used below for Library types), so a
// bare "Application" identifier resolves to the namespace, not Microsoft.UI.Xaml.Application —
// alias to a distinct name instead (see AGENTS.md).
using XamlApplication = Microsoft.UI.Xaml.Application;
using PlaySphere.Application.Library;
using PlaySphere.Domain.Library;
using PlaySphere.UI.Helpers;
using PlaySphere.UI.Navigation;

namespace PlaySphere.UI.Pages;

public sealed partial class PlaylistsPage : Page
{
    private readonly IMediaLibraryRepository? _repository = PlaySphereServices.GetOptional<IMediaLibraryRepository>();
    private readonly IM3uImporter? _m3uImporter = PlaySphereServices.GetOptional<IM3uImporter>();
    private readonly IM3uExporter? _m3uExporter = PlaySphereServices.GetOptional<IM3uExporter>();

    private sealed record PlaylistEntry(Playlist Playlist, int Count)
    {
        public string DisplayName => Playlist.Name;
        public string Subtitle => $"{Count} item{(Count == 1 ? "" : "s")}";
    }

    private sealed record ItemEntry(PlaylistItem PlaylistItem, LibraryMediaItem MediaItem)
    {
        public string Title => string.IsNullOrWhiteSpace(MediaItem.Title)
            ? System.IO.Path.GetFileName(MediaItem.FilePath)
            : MediaItem.Title;
        public string Subtitle => System.IO.Path.GetFileName(MediaItem.FilePath);
    }

    private IReadOnlyList<LibraryMediaItem> _catalog = [];
    private readonly List<PlaylistEntry> _entries = new();
    private Playlist? _selectedPlaylist;

    public PlaylistsPage() => InitializeComponent();

    private async void Page_Loaded(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        if (_repository == null) return;

        _catalog = await _repository.GetAllMediaItemsAsync();
        PopulateAddItemCombo();

        var playlists = await _repository.GetPlaylistsAsync();
        _entries.Clear();
        foreach (var p in playlists)
        {
            var items = await _repository.GetPlaylistItemsAsync(p.Id);
            _entries.Add(new PlaylistEntry(p, items.Count));
        }

        PlaylistList.ItemsSource = _entries.ToList();
        PlaylistsEmptyCard.Visibility = _entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // Restore selection if the previously selected playlist still exists.
        if (_selectedPlaylist != null)
        {
            var match = _entries.FirstOrDefault(e => e.Playlist.Id == _selectedPlaylist.Id);
            if (match != null)
            {
                PlaylistList.SelectedItem = match;
            }
            else
            {
                _selectedPlaylist = null;
                ClearDetail();
            }
        }
        else if (_entries.Count == 0)
        {
            ClearDetail();
        }
    }

    private void PopulateAddItemCombo()
    {
        AddItemComboBox.Items.Clear();
        foreach (var item in _catalog)
        {
            var label = string.IsNullOrWhiteSpace(item.Title)
                ? System.IO.Path.GetFileName(item.FilePath)
                : item.Title;
            AddItemComboBox.Items.Add(new ComboBoxItem
            {
                Content = label,
                Tag = item.Id,
            });
        }
    }

    private async void PlaylistList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PlaylistList.SelectedItem is PlaylistEntry entry)
        {
            _selectedPlaylist = entry.Playlist;
            await LoadDetailAsync(entry.Playlist.Id);
        }
    }

    private async Task LoadDetailAsync(int playlistId)
    {
        if (_repository == null || _selectedPlaylist == null) return;

        var playlistItems = await _repository.GetPlaylistItemsAsync(playlistId);

        // Map MediaItemId -> LibraryMediaItem (preserve playlist order).
        var byId = _catalog.ToDictionary(i => i.Id);
        var entries = new List<ItemEntry>();
        foreach (var pi in playlistItems)
        {
            if (byId.TryGetValue(pi.MediaItemId, out var media))
            {
                entries.Add(new ItemEntry(pi, media));
            }
        }

        DetailTitle.Text = _selectedPlaylist.Name;
        DetailCount.Text = $"{entries.Count} item{(entries.Count == 1 ? "" : "s")}";
        ItemsEmptyCard.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        AddItemButton.IsEnabled = true;
        RenamePlaylistButton.IsEnabled = true;
        DeletePlaylistButton.IsEnabled = true;
        PlayPlaylistButton.IsEnabled = true;
        ExportPlaylistButton.IsEnabled = true;

        PopulateItems(entries);
    }

    private void PopulateItems(IReadOnlyList<ItemEntry> entries)
    {
        ItemsPanel.Children.Clear();

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];

            var card = new Grid
            {
                CornerRadius = new CornerRadius(10),
                Background = (Microsoft.UI.Xaml.Media.Brush)XamlApplication.Current.Resources["BrushSurfaceCard"],
                Padding = new Thickness(12, 8, 12, 8),
                ColumnSpacing = 8,
            };
            card.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            card.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            card.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var indexText = new TextBlock
            {
                Text = (index + 1).ToString(),
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)XamlApplication.Current.Resources["BrushTextMuted"],
            };
            card.Children.Add(indexText);

            var texts = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(new TextBlock
            {
                Text = entry.Title,
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextTrimming = Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)XamlApplication.Current.Resources["BrushTextPrimary"],
            });
            texts.Children.Add(new TextBlock
            {
                Text = entry.Subtitle,
                FontSize = 11,
                TextTrimming = Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)XamlApplication.Current.Resources["BrushTextMuted"],
            });
            Grid.SetColumn(texts, 1);
            card.Children.Add(texts);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            actions.Children.Add(MakeIconButton('\uE70E', "Move up", async () => await MoveItemAsync(entry, -1)));
            actions.Children.Add(MakeIconButton('\uE70D', "Move down", async () => await MoveItemAsync(entry, +1)));
            actions.Children.Add(MakeIconButton('\uE74D', "Remove from playlist", async () => await RemoveItemAsync(entry)));
            Grid.SetColumn(actions, 2);
            card.Children.Add(actions);

            ItemsPanel.Children.Add(card);
        }
    }

    private Button MakeIconButton(char glyph, string tooltip, Action onClick)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = glyph.ToString(), FontSize = 13 },
            FontSize = 12,
            Padding = new Thickness(6),
            VerticalAlignment = VerticalAlignment.Center,
            Background = (Microsoft.UI.Xaml.Media.Brush)XamlApplication.Current.Resources["BrushSurfaceHover"],
        };
        ToolTipService.SetToolTip(button, tooltip);
        button.Click += (_, _) => onClick();
        return button;
    }

    private async Task MoveItemAsync(ItemEntry entry, int delta)
    {
        if (_repository == null || _selectedPlaylist == null) return;

        var items = await _repository.GetPlaylistItemsAsync(_selectedPlaylist.Id);
        var ordered = items.OrderBy(i => i.Position).ToList();
        var idx = ordered.FindIndex(i => i.Id == entry.PlaylistItem.Id);
        var target = idx + delta;
        if (idx < 0 || target < 0 || target >= ordered.Count) return;

        (ordered[idx], ordered[target]) = (ordered[target], ordered[idx]);

        var ids = ordered.Select(i => i.MediaItemId).ToList();
        await _repository.ReorderPlaylistAsync(_selectedPlaylist.Id, ids);
        await ReloadDetail();
    }

    private async Task RemoveItemAsync(ItemEntry entry)
    {
        if (_repository == null || _selectedPlaylist == null) return;

        await _repository.RemoveFromPlaylistAsync(entry.PlaylistItem.Id);
        await ReloadDetail();
    }

    private async Task ReloadDetail()
    {
        if (_selectedPlaylist != null)
        {
            await LoadDetailAsync(_selectedPlaylist.Id);
        }
    }

    private async void CreatePlaylistButton_Click(object sender, RoutedEventArgs e)
    {
        var name = NewPlaylistNameBox.Text?.Trim();
        if (_repository == null || string.IsNullOrWhiteSpace(name)) return;

        var playlist = await _repository.CreatePlaylistAsync(name);
        NewPlaylistNameBox.Text = string.Empty;
        await RefreshAsync();

        // Select the freshly created playlist.
        var created = _entries.FirstOrDefault(x => x.Playlist.Id == playlist.Id);
        if (created != null) PlaylistList.SelectedItem = created;
    }

    private void NewPlaylistNameBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            CreatePlaylistButton_Click(sender, new RoutedEventArgs());
        }
    }

    private async void RenamePlaylistButton_Click(object sender, RoutedEventArgs e)
    {
        if (_repository == null || _selectedPlaylist == null) return;

        var dialog = new ContentDialog
        {
            Title = "Rename playlist",
            PrimaryButtonText = "Rename",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            Content = new TextBox { Text = _selectedPlaylist.Name, PlaceholderText = "New name" },
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && dialog.Content is TextBox box)
        {
            var name = box.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(name))
            {
                await _repository.RenamePlaylistAsync(_selectedPlaylist.Id, name);
                await RefreshAsync();
            }
        }
    }

    private async void DeletePlaylistButton_Click(object sender, RoutedEventArgs e)
    {
        if (_repository == null || _selectedPlaylist == null) return;

        var dialog = new ContentDialog
        {
            Title = "Delete playlist",
            Content = $"Delete \"{_selectedPlaylist.Name}\"? Its items will be removed from the playlist (your library is untouched).",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            await _repository.DeletePlaylistAsync(_selectedPlaylist.Id);
            _selectedPlaylist = null;
            ClearDetail();
            await RefreshAsync();
        }
    }

    private async void AddItemButton_Click(object sender, RoutedEventArgs e)
    {
        if (_repository == null || _selectedPlaylist == null || AddItemComboBox.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        if (item.Tag is int mediaItemId)
        {
            await _repository.AddToPlaylistAsync(_selectedPlaylist.Id, mediaItemId);
            await LoadDetailAsync(_selectedPlaylist.Id);
            await RefreshPlaylistCountAsync(_selectedPlaylist.Id);
        }
    }

    private async void PlayPlaylistButton_Click(object sender, RoutedEventArgs e)
    {
        if (_repository == null || _selectedPlaylist == null) return;

        var items = await _repository.GetPlaylistItemsAsync(_selectedPlaylist.Id);
        var byId = _catalog.ToDictionary(i => i.Id);
        var paths = new List<string>();
        foreach (var pi in items.OrderBy(i => i.Position))
        {
            if (byId.TryGetValue(pi.MediaItemId, out var media))
            {
                paths.Add(media.FilePath);
            }
        }

        if (paths.Count == 0) return;

        Frame?.Navigate(typeof(PlayerPage), new PlayerQueuePayload(paths));
    }

    private async void ImportM3uButton_Click(object sender, RoutedEventArgs e)
    {
        if (_m3uImporter == null)
        {
            ShowMessage("M3U import is not available.", "Import M3U");
            return;
        }

        var sourcePath = await FilePickerHelper.PickM3uFileAsync();
        if (string.IsNullOrWhiteSpace(sourcePath)) return;

        try
        {
            ImportM3uButton.IsEnabled = false;
            var result = await _m3uImporter.ImportAsync(sourcePath);
            await RefreshAsync();

            var created = _entries.FirstOrDefault(x => x.Playlist.Id == result.Playlist.Id);
            if (created != null) PlaylistList.SelectedItem = created;

            ShowMessage(
                $"Imported \"{result.Playlist.Name}\": {result.Imported} item(s) added, {result.Skipped} skipped.",
                "Import M3U");
        }
        catch (Exception ex)
        {
            ShowMessage($"Import failed: {ex.Message}", "Import M3U");
        }
        finally
        {
            ImportM3uButton.IsEnabled = true;
        }
    }

    private async void ExportPlaylistButton_Click(object sender, RoutedEventArgs e)
    {
        if (_m3uExporter == null || _repository == null || _selectedPlaylist == null) return;

        var safeName = string.Join("_", _selectedPlaylist.Name.Split(System.IO.Path.GetInvalidFileNameChars()))
            .Trim();
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "playlist";

        var destination = await FilePickerHelper.PickSaveM3uFileAsync($"{safeName}.m3u");
        if (string.IsNullOrWhiteSpace(destination)) return;

        try
        {
            var items = await _repository.GetPlaylistItemsAsync(_selectedPlaylist.Id);
            var byId = _catalog.ToDictionary(i => i.Id);
            var paths = new List<string>();
            foreach (var pi in items.OrderBy(i => i.Position))
            {
                if (byId.TryGetValue(pi.MediaItemId, out var media))
                {
                    paths.Add(media.FilePath);
                }
            }

            var ok = await _m3uExporter.ExportAsync(paths, destination);
            ShowMessage(ok ? $"Exported {paths.Count} item(s) to M3U." : "Export failed.", "Export M3U");
        }
        catch (Exception ex)
        {
            ShowMessage($"Export failed: {ex.Message}", "Export M3U");
        }
    }

    private async void ShowMessage(string message, string title)
    {
        try
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = XamlRoot,
            };
            await dialog.ShowAsync();
        }
        catch
        {
            // No window root available (e.g. page navigating away) — ignore.
        }
    }

    private Task RefreshPlaylistCountAsync(int playlistId)
    {
        // RefreshAsync reloads every playlist's item count and restores the current selection.
        return RefreshAsync();
    }

    private void ClearDetail()
    {
        DetailTitle.Text = "Select a playlist";
        DetailCount.Text = "Pick a playlist from the left to manage it.";
        ItemsPanel.Children.Clear();
        ItemsEmptyCard.Visibility = Visibility.Collapsed;
    }
}
