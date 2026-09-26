using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PlaySphere.Domain.Media;
using PlaySphere.Playback.Abstractions;
using PlaySphere.UI.Controls;
using PlaySphere.UI.Navigation;
using PlaySphere.UI.Services;

namespace PlaySphere.UI.Shell;

public sealed partial class AppShell : Page
{
    private const string PlayGlyph = "";
    private const string PauseGlyph = "";

    private readonly NavigationService _navigation;
    private readonly IWindowService? _windowService;
    private readonly IMediaPlaybackEngine? _engine;

    /// <summary>
    /// The single, app-lifetime shell instance (this app has exactly one window/shell). Lets
    /// <see cref="Pages.PlayerPage"/> reach the shared <see cref="VideoHost"/> without a project-level
    /// dependency between the two (ADR-0012).
    /// </summary>
    public static AppShell? Current { get; private set; }

    /// <summary>The shell-owned, app-lifetime video render surface (ADR-0012).</summary>
    public VideoSurfaceHost VideoHost => SharedVideoHost;

    public AppShell()
    {
        InitializeComponent();
        Current = this;
        _navigation = new NavigationService();
        _windowService = PlaySphereServices.GetOptional<IWindowService>();
        _engine = PlaySphereServices.GetOptional<IMediaPlaybackEngine>();

        if (_windowService != null)
        {
            _windowService.FullScreenChanged += WindowService_FullScreenChanged;
            _windowService.CinemaModeChanged += WindowService_CinemaModeChanged;
            _windowService.CompactOverlayChanged += WindowService_CompactOverlayChanged;
        }

        if (_engine != null)
        {
            _engine.StateChanged += Engine_StateChanged;
            _engine.TracksChanged += Engine_TracksChanged;
        }

        ContentFrame.Navigated += ContentFrame_Navigated;

        foreach (var item in ShellMenu.Items)
        {
            RootNavigation.MenuItems.Add(new NavigationViewItem
            {
                Content = item.Label,
                Icon = new SymbolIcon(item.Icon),
                Tag = item.Key,
            });
        }

        _navigation.Navigate(ContentFrame, AppRoutes.Home);
        RootNavigation.SelectedItem = RootNavigation.MenuItems[0];
    }

    private void ContentFrame_Navigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        UpdatePaneVisibility();
        UpdateNowPlayingBar();
    }

    private void Engine_StateChanged(object? sender, PlaybackStateChangedEventArgs e)
    {
        DispatcherQueue?.TryEnqueue(UpdateNowPlayingBar);
    }

    private void Engine_TracksChanged(object? sender, PlaybackTracksChangedEventArgs e)
    {
        DispatcherQueue?.TryEnqueue(UpdateNowPlayingBar);
    }

    /// <summary>
    /// Shows/hides and refreshes the persistent "now playing" bar. Visible only when the engine
    /// has media loaded and isn't Empty/Stopped, and only while the player page itself (which
    /// already shows full transport controls) isn't the current view.
    /// </summary>
    private void UpdateNowPlayingBar()
    {
        var isPlayerPage = ContentFrame.Content is Pages.PlayerPage;
        var hasMedia = _engine?.LoadedFilePath != null
            && _engine.State is not PlaybackState.Empty
            and not PlaybackState.Stopped
            and not PlaybackState.Error;

        // The bar is only hidden on the PlayerPage itself (which has full transport controls).
        // Fullscreen / compact-overlay do NOT hide it — navigating Home from fullscreen must leave
        // the bar visible so the user can tap it to return to the player; hiding it in fullscreen
        // left the user stranded with no way back. It is also kept visible while its state is
        // Loading/Buffering (e.g. a reconnecting stream), so a transient stall never strands the
        // session with no way back to the player.
        if (isPlayerPage || !hasMedia)
        {
            NowPlayingBar.Visibility = Visibility.Collapsed;
            return;
        }

        NowPlayingBar.Visibility = Visibility.Visible;
        NowPlayingTitle.Text = MediaFile.FromPathOrUri(_engine!.LoadedFilePath!).DisplayTitle;

        var isPlaying = _engine.State == PlaybackState.Playing;
        NowPlayingPlayPauseIcon.Glyph = isPlaying ? PauseGlyph : PlayGlyph;
        ToolTipService.SetToolTip(NowPlayingPlayPauseButton, isPlaying ? "Pause" : "Play");
    }

    private async void NowPlayingPlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_engine == null)
        {
            return;
        }

        if (_engine.State == PlaybackState.Playing)
        {
            await _engine.PauseAsync();
        }
        else
        {
            await _engine.PlayAsync();
        }
    }

    private void NowPlayingBar_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (_engine == null)
        {
            return;
        }

        _navigation.Navigate(ContentFrame, AppRoutes.Player, _engine);
    }

    private void WindowService_FullScreenChanged(object? sender, EventArgs e)
    {
        DispatcherQueue?.TryEnqueue(UpdatePaneVisibility);
    }

    private void WindowService_CinemaModeChanged(object? sender, EventArgs e)
    {
        DispatcherQueue?.TryEnqueue(UpdatePaneVisibility);
    }

    private void WindowService_CompactOverlayChanged(object? sender, EventArgs e)
    {
        DispatcherQueue?.TryEnqueue(UpdatePaneVisibility);
    }

    private void UpdatePaneVisibility()
    {
        var isPlayer = ContentFrame.Content is Pages.PlayerPage;

        // Hide the navigation pane ONLY while on the PlayerPage itself. Fullscreen / cinema /
        // compact overlay on non-player pages must not hide the pane — doing so left the user
        // stranded on the Home page with no navigation controls and no Now Playing bar to return
        // to the player.
        RootNavigation.IsPaneVisible = !isPlayer;
    }

    private void RootNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: string route })
        {
            _navigation.Navigate(ContentFrame, route);
        }
    }

    /// <summary>
    /// Re-parents the shared <see cref="VideoHost"/> into <paramref name="target"/> (ADR-0012).
    /// Safe to call repeatedly with the same target; removes it from wherever it currently lives
    /// first (its parking spot, or a previous <c>PlayerPage</c> instance's slot) so a control can
    /// only ever have one visual parent.
    /// </summary>
    public void AttachVideoHostTo(Panel target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (SharedVideoHost.Parent is Panel currentParent && !ReferenceEquals(currentParent, target))
        {
            currentParent.Children.Remove(SharedVideoHost);
        }
        if (!target.Children.Contains(SharedVideoHost))
        {
            target.Children.Add(SharedVideoHost);
        }
    }

    /// <summary>Returns the shared <see cref="VideoHost"/> to its parking spot (called when <c>PlayerPage</c> unloads).</summary>
    public void ParkVideoHost() => AttachVideoHostTo(VideoHostParkingSpot);
}