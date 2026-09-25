using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
// PlaySphere.Application (the project) is a reachable namespace here (a sibling of
// PlaySphere.UI.Pages under the shared PlaySphere ancestor), which wins simple-name resolution
// for a bare "Application" identifier even over a same-named using-alias — namespace-member
// lookup across enclosing namespaces happens before using directives are consulted. Alias to a
// distinct name instead of "Application" and use that below.
using XamlApplication = Microsoft.UI.Xaml.Application;
using PlaySphere.Application.Visualizer;
using PlaySphere.Domain.Media;
using PlaySphere.Playback.Abstractions;
using PlaySphere.Subtitles;
using PlaySphere.UI.Controls;
using PlaySphere.UI.Helpers;
using PlaySphere.UI.Models;
using PlaySphere.UI.Navigation;
using PlaySphere.UI.Services;
using PlaySphere.UI.ViewModels;
using PlaySphere.UI.Visualizer;
using Windows.System;
using Windows.UI.ViewManagement;

namespace PlaySphere.UI.Pages;

public sealed partial class PlayerPage : Page
{
    private const string PlayGlyph = "";
    private const string PauseGlyph = "";
    private const string VolumeGlyph = "";
    private const string MuteGlyph = "";
    private const string FullScreenGlyph = "";
    private const string BackToWindowGlyph = "";

    private static readonly TimeSpan ShortSeek = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LongSeek = TimeSpan.FromSeconds(30);

    private readonly IWindowService? _windowService = PlaySphereServices.GetOptional<IWindowService>();
    private PlayerViewModel? _viewModel;
    private IMediaPlaybackEngine? _engine;
    private ILatestAnalysisFrameProvider? _visualizerFrames;
    private IVisualizerAudioSource? _visualizerSource;
    private bool _visualizerEnabled;
    private readonly UISettings _uiSettings = new();
    private bool _videoSurfaceAttached;
    private bool _isUpdatingSliderFromVm;
    private bool _isDraggingSlider;
    private bool _isDraggingSubtitleSlider;
    private bool _isDraggingAudioSlider;
    private bool _isPopulatingDrawer;
    private bool _isPointerOverControls;
    private DispatcherTimer? _stereoBalanceDebounceTimer;
    private int _pendingStereoBalanceValue;

    // Tracks the in-flight ViewModel.LoadAsync/LoadCollectionAsync call so AttachVideoHostAsync can
    // wait for it (see the comment there): the VideoView's own native init fires VideoHost_Initialized
    // independently of load progress, so without this a video-surface rebuild can start concurrently
    // with LoadAsync's resume-to-last-position seek — both touch the same native MediaPlayer from
    // different threads (LoadAsync's continuation runs on whatever thread libVLC's parse-callback
    // uses) and deadlocked inside libVLC itself, reproduced via a live process dump.
    private Task? _loadTask;

    private string? _lastThumbnailStripPath;
    private DispatcherTimer? _autoHideTimer;

    public PlayerPage()
    {
        InitializeComponent();
        VisualizerSurface.ReducedMotion = !_uiSettings.AnimationsEnabled;
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            _uiSettings.AnimationsEnabledChanged += UiSettings_AnimationsEnabledChanged;
        }
    }

    public PlayerViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            }

            _viewModel = value;

            if (_viewModel != null)
            {
                _viewModel.PropertyChanged += ViewModel_PropertyChanged;
                UpdateUiFromViewModel();
            }
        }
    }

    // ── Lifecycle ───────────────────────────────────────────────────────────

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _autoHideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3.0)
        };
        _autoHideTimer.Tick += AutoHideTimer_Tick;
        _autoHideTimer.Start();

        if (_windowService != null)
        {
            _windowService.FullScreenChanged += WindowService_FullScreenChanged;
            _windowService.CinemaModeChanged += WindowService_CinemaModeChanged;
            _windowService.CompactOverlayChanged += WindowService_CompactOverlayChanged;
            UpdateWindowModeButtons();
        }

        RootPlayerGrid.Focus(FocusState.Programmatic);
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_autoHideTimer != null)
        {
            _autoHideTimer.Stop();
            _autoHideTimer.Tick -= AutoHideTimer_Tick;
            _autoHideTimer = null;
        }

        if (_stereoBalanceDebounceTimer != null)
        {
            _stereoBalanceDebounceTimer.Stop();
            _stereoBalanceDebounceTimer.Tick -= StereoBalanceDebounceTimer_Tick;
            _stereoBalanceDebounceTimer = null;
        }

        // Leaving the player should return the window to a normal, chrome-bearing state so the user
        // can still close/minimize/resize from whichever page they navigate to. Fullscreen/cinema
        // are player-local immersive modes; carrying them onto Home (or elsewhere) left the window
        // with no titlebar and no way to manipulate it until going back to the player.
        ExitWindowModes();

        if (_windowService != null)
        {
            _windowService.FullScreenChanged -= WindowService_FullScreenChanged;
            _windowService.CinemaModeChanged -= WindowService_CinemaModeChanged;
            _windowService.CompactOverlayChanged -= WindowService_CompactOverlayChanged;
        }

        if (_viewModel != null)
        {
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _viewModel.Dispose();
            _viewModel = null;
        }

        // Detach the renderer so a stale surface no longer holds our MediaPlayer.
        if (_videoSurfaceAttached)
        {
            _videoSurfaceAttached = false;
            _ = _engine?.DetachVideoSurfaceAsync();
        }

        // Stop the visualizer's audio source (ADR-0009: WASAPI device-loopback on Windows, so
        // leaving the page stops the capture but never silences libVLC's normal speaker output).
        if (_visualizerEnabled)
        {
            _visualizerEnabled = false;
            _visualizerSource?.Stop();
        }

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            _uiSettings.AnimationsEnabledChanged -= UiSettings_AnimationsEnabledChanged;
        }
    }

    private void VideoHost_Initialized(object? sender, EventArgs e)
    {
        // Swap-chain native pointers are only valid after the VideoView has initialized; attach
        // the engine renderer now that VideoHost.SwapChainOptions is populated. This MUST be
        // deferred (not run synchronously here): AttachVideoHostAsync can tear down and rebuild
        // the native LibVLC player and then reassign this same SwapChainPanel's renderer, and
        // doing that reentrantly from inside the panel's own native Initialized callback is what
        // produced a CoreMessagingXP.dll native crash on every media open (first video/audio open
        // always needs this attach). Posting via DispatcherQueue lets the Initialized call stack
        // fully unwind before the native rebuild/SetRenderer runs.
        DispatcherQueue?.TryEnqueue(async () => await AttachVideoHostAsync());
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        _engine = PlaySphereServices.GetOptional<IMediaPlaybackEngine>();
        _visualizerFrames = PlaySphereServices.GetOptional<ILatestAnalysisFrameProvider>();
        _visualizerSource = PlaySphereServices.GetOptional<IVisualizerAudioSource>();

        // Bind the visualizer to the pure analysis-frame source. The audio source (ADR-0009: WASAPI
        // device-loopback on Windows) is started when the visualizer is enabled — it captures the
        // audio the device is playing and feeds it to the analyser without silencing the speakers.
        if (_visualizerFrames != null)
        {
            VisualizerSurface.FrameProvider = _visualizerFrames;
            VisualizerSurface.Preset = new SpectrumFlowPreset();
        }

        var libraryRepository = PlaySphereServices.GetOptional<PlaySphere.Application.Library.IMediaLibraryRepository>();
        var preferences = PlaySphereServices.GetOptional<PlaySphere.Application.Preferences.IPreferencesStore>();
        var clipExporter = PlaySphereServices.GetOptional<PlaySphere.Application.Media.IMediaClipExporter>();
        var mediaConverter = PlaySphereServices.GetOptional<PlaySphere.Application.Media.IMediaConverter>();
        var thumbnailProvider = PlaySphereServices.GetOptional<PlaySphere.Application.Media.IMediaThumbnailProvider>();
        var waveformProvider = PlaySphereServices.GetOptional<PlaySphere.Application.Media.IWaveformProvider>();
        var generateChaptersRunner = PlaySphereServices.GetOptional<PlaySphere.Application.AI.IGenerateChaptersRunner>();
        var stereoBalanceSession = PlaySphereServices.GetOptional<PlaySphere.Application.Media.IStereoBalanceAudioSession>();

        if (e.Parameter is PlayerViewModel vm)
        {
            ViewModel = vm;
        }
        else if (e.Parameter is IMediaPlaybackEngine engine)
        {
            ViewModel = new PlayerViewModel(engine, libraryRepository, preferences: preferences, clipExporter: clipExporter, mediaConverter: mediaConverter, thumbnailProvider: thumbnailProvider, waveformProvider: waveformProvider, generateChaptersRunner: generateChaptersRunner, stereoBalanceSession: stereoBalanceSession);
        }
        else if (e.Parameter is string filePath)
        {
            var resolvedEngine = _engine;
            if (resolvedEngine != null)
            {
                ViewModel = new PlayerViewModel(resolvedEngine, libraryRepository, preferences: preferences, clipExporter: clipExporter, mediaConverter: mediaConverter, thumbnailProvider: thumbnailProvider, generateChaptersRunner: generateChaptersRunner, stereoBalanceSession: stereoBalanceSession);
                var loadTask = ViewModel.LoadAsync(filePath);
                _loadTask = loadTask;
                await loadTask;
            }
        }
        else if (e.Parameter is PlayerQueuePayload queue)
        {
            var resolvedEngine = _engine;
            if (resolvedEngine != null)
            {
                ViewModel = new PlayerViewModel(resolvedEngine, libraryRepository, preferences: preferences, clipExporter: clipExporter, mediaConverter: mediaConverter, thumbnailProvider: thumbnailProvider, generateChaptersRunner: generateChaptersRunner, stereoBalanceSession: stereoBalanceSession);
                var loadTask = ViewModel.LoadCollectionAsync(queue.FilePaths, queue.StartFilePath);
                _loadTask = loadTask;
                await loadTask;

                // Transcript/timestamp navigation (AI-05): jump to the requested moment best-effort.
                if (queue.Position.HasValue)
                {
                    await ViewModel.SeekToPositionAsync(queue.Position.Value);
                }
            }
        }

        // If the video surface already initialized before navigation (unlikely on first load,
        // but possible on re-entry), attach now. Otherwise VideoHost_Initialized handles it.
        if (_videoSurfaceAttached is false && VideoHost.SwapChainOptions != null)
        {
            await AttachVideoHostAsync();
        }
    }

    /// <summary>
    /// Binds the playback engine's video renderer to the <see cref="VideoHost"/> surface so decoded
    /// frames draw into the swap chain. Called once the underlying VideoView's swap-chain is ready.
    /// </summary>
    private async Task AttachVideoHostAsync()
    {
        if (_engine == null || _videoSurfaceAttached)
        {
            return;
        }

        _videoSurfaceAttached = true;

        // Wait for any in-flight LoadAsync/LoadCollectionAsync to finish first (see _loadTask's
        // comment). Its continuation — including the resume-to-last-position seek — can run on
        // whatever thread libVLC's native parse-callback uses; starting the rebuild below
        // concurrently with that raced two native MediaPlayer calls into a libVLC-internal
        // deadlock (Stop() vs SetPosition()), reproduced via a live process dump. Sequencing
        // instead of locking, since the deadlock was inside libVLC's own Stop() implementation —
        // opaque native internals a managed-only lock cannot reach.
        if (_loadTask is { } pendingLoad)
        {
            try
            {
                await pendingLoad;
            }
            catch
            {
                // LoadAsync surfaces its own failures; don't let that skip the attach below.
            }
        }

        UpdateVideoPlaceholderUi();

        // The first video-surface attach in a session rebuilds libVLC's native player from
        // scratch (it must be constructed with swap-chain pointers that only exist once the
        // VideoView has initialized), which runs synchronously and can take a few seconds.
        // Surface that as a visible loading state so it reads as "loading", not a frozen app.
        // Play is disabled for the same span: libVLC's MediaPlayer.Play() fails immediately
        // (returns false, no visible error) if called before this attach has bound a video
        // renderer — pressing Play during this window silently landed the engine in its Error
        // state (this was the root cause behind most "Play() returned false" reports).
        AttachingSurfacePanel.Visibility = Visibility.Visible;
        PlayPauseButton.IsEnabled = false;

        try
        {
            await _engine.AttachVideoSurfaceAsync(VideoHost);
        }
        catch (Exception ex)
        {
            // A failed swap-chain bind must not crash the player; leave the surface unbound
            // (audio/controls keep working) and surface the placeholder text.
            _videoSurfaceAttached = false;
            UpdateVideoPlaceholderUi();
            System.Diagnostics.Debug.WriteLine($"VideoSurface attach failed: {ex}");
        }
        finally
        {
            AttachingSurfacePanel.Visibility = Visibility.Collapsed;
            PlayPauseButton.IsEnabled = true;
        }
    }

    // ── Window Mode Handling (PS-205, PS-206) ──────────────────────────────

    private void UiSettings_AnimationsEnabledChanged(UISettings sender, object args)
    {
        VisualizerSurface.ReducedMotion = !sender.AnimationsEnabled;
    }

    private void WindowService_FullScreenChanged(object? sender, EventArgs e)
    {
        DispatcherQueue?.TryEnqueue(() =>
        {
            UpdateWindowModeButtons();
        });
    }

    private void WindowService_CinemaModeChanged(object? sender, EventArgs e)
    {
        DispatcherQueue?.TryEnqueue(UpdateWindowModeButtons);
    }

    private void WindowService_CompactOverlayChanged(object? sender, EventArgs e)
    {
        DispatcherQueue?.TryEnqueue(UpdateWindowModeButtons);
    }

    private void UpdateWindowModeButtons()
    {
        if (_windowService == null) return;

        var isFullScreen = _windowService.IsFullScreen;
        FullScreenButton.Content = isFullScreen ? BackToWindowGlyph : FullScreenGlyph;
        ToolTipService.SetToolTip(FullScreenButton, isFullScreen ? "Exit Fullscreen (Esc / F11)" : "Fullscreen (F / F11)");

        var isCinema = _windowService.IsCinemaMode;
        var isCompact = _windowService.IsCompactOverlay;

        if (XamlApplication.Current.Resources.TryGetValue("BrushAccentCyan", out var cyanBrush) &&
            XamlApplication.Current.Resources.TryGetValue("BrushTextSecondary", out var secondaryBrush))
        {
            var cyan = (Microsoft.UI.Xaml.Media.Brush)cyanBrush;
            var secondary = (Microsoft.UI.Xaml.Media.Brush)secondaryBrush;

            CinemaButton.Foreground = isCinema ? cyan : secondary;
            MiniPlayerButton.Foreground = isCompact ? cyan : secondary;
        }

        ToolTipService.SetToolTip(CinemaButton, isCinema ? "Exit Cinema Mode (C)" : "Cinema Mode (C)");
        ToolTipService.SetToolTip(MiniPlayerButton, isCompact ? "Exit Mini-Player (P)" : "Mini-Player / Picture-in-Picture (P)");

        // In compact overlay mode, hide the top bar and simplify
        TopNavBar.Visibility = isCompact ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ToggleFullScreen()
    {
        PauseVisualizerAcrossWindowChange();
        _windowService?.ToggleFullScreen();
    }

    private void ToggleCinemaMode()
    {
        _windowService?.ToggleCinemaMode();
    }

    private void ToggleCompactOverlay()
    {
        PauseVisualizerAcrossWindowChange();
        _windowService?.ToggleCompactOverlay();
    }

    /// <summary>
    /// Pauses the Win2D visualizer render loop before a window presenter change (fullscreen /
    /// compact-overlay) resizes the underlying swap chain, then resumes it once layout has
    /// settled. A presenter change resizes the window synchronously while Win2D's own animation
    /// thread may still be mid-draw; racing a swap-chain resize against an in-flight Draw call is
    /// a known source of native access violations in Microsoft.Graphics.Canvas.dll.
    /// </summary>
    private void PauseVisualizerAcrossWindowChange()
    {
        VisualizerSurface.IsPaused = true;
        DispatcherQueue?.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, UpdateVisualizerUi);
    }

    /// <summary>
    /// Restores the window to its normal (frame-bearing) presentation: exits fullscreen, cinema and
    /// compact-overlay so the system titlebar/chrome returns. Pauses the visualizer across the
    /// presenter changes to avoid racing a swap-chain resize against Win2D's render thread. Called on
    /// page unload so navigating away from the player never strands the window in an immersive mode
    /// with no way to close/minimize/resize.
    /// </summary>
    private void ExitWindowModes()
    {
        PauseVisualizerAcrossWindowChange();

        if (_windowService == null) return;

        try
        {
            if (_windowService.IsFullScreen)
            {
                _windowService.SetFullScreen(false);
            }

            if (_windowService.IsCinemaMode)
            {
                _windowService.SetCinemaMode(false);
            }

            if (_windowService.IsCompactOverlay)
            {
                _windowService.SetCompactOverlay(false);
            }
        }
        catch (Exception ex)
        {
            // A presenter-teardown fault must not block navigation.
            System.Diagnostics.Debug.WriteLine($"ExitWindowModes failed: {ex}");
        }
    }

    // ── ViewModel Binding ───────────────────────────────────────────────────

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        DispatcherQueue?.TryEnqueue(() =>
        {
            UpdateUiFromViewModel();
            if (_viewModel?.IsPlaying != true || _viewModel?.IsDrawerOpen == true)
            {
                ShowControls();
            }
        });
    }

    private void UpdateUiFromViewModel()
    {
        if (_viewModel == null) return;

        TitleText.Text = _viewModel.Title;
        StateText.Text = _viewModel.StateLabel;
        if (_viewModel.LastError is { } err)
        {
            // Surface the real playback failure so it is visible instead of a bare "Error" label.
            StateText.Text = err;
            LogPlaybackError(err);
        }
        PositionText.Text = _viewModel.PositionText;
        UpdateLiveIndicator();
        RateText.Text = _viewModel.RateText;

        PlayPauseButton.Content = _viewModel.IsPlaying ? PauseGlyph : PlayGlyph;
        ToolTipService.SetToolTip(PlayPauseButton, _viewModel.IsPlaying ? "Pause (Space)" : "Play (Space)");

        MuteButton.Content = _viewModel.IsMuted ? MuteGlyph : VolumeGlyph;
        ToolTipService.SetToolTip(MuteButton, _viewModel.IsMuted ? "Unmute (M)" : "Mute (M)");

        _isUpdatingSliderFromVm = true;
        try
        {
            TimelineSlider.IsEnabled = _viewModel.IsSeekable
                && _viewModel.StateLabel is not "Empty" and not "Loading" and not "Error";
            if (!_isDraggingSlider)
            {
                TimelineSlider.Value = _viewModel.PositionPercent;
            }
            VolumeSlider.Value = _viewModel.Volume;

            SubtitleDelayValueText.Text = _viewModel.SubtitleDelayText;
            if (!_isDraggingSubtitleSlider)
            {
                SubtitleDelaySlider.Value = _viewModel.SubtitleDelayMilliseconds;
            }

            AudioDelayValueText.Text = _viewModel.AudioDelayText;
            if (!_isDraggingAudioSlider)
            {
                AudioDelaySlider.Value = _viewModel.AudioDelayMilliseconds;
            }
        }
        finally
        {
            _isUpdatingSliderFromVm = false;
        }

        UpdateQueueTransportUi();
        UpdateDrawerUi();
        UpdateMarkUi();
        UpdateConvertUi();
        UpdateWaveformUi();
        RefreshVisualizerAvailability();
    }

    /// <summary>
    /// Shows a "LIVE" badge (and hides the finite duration) for interminable / non-seekable
    /// streams such as radio, and disables the ±10s skip buttons which are meaningless there.
    /// </summary>
    private void UpdateLiveIndicator()
    {
        if (_viewModel == null) return;

        var live = !_viewModel.IsSeekable && _viewModel.StateLabel is not "Empty" and not "Error";
        LiveBadge.Visibility = live ? Visibility.Visible : Visibility.Collapsed;
        DurationText.Text = _viewModel.IsSeekable ? _viewModel.DurationText : "0:00";
        DurationText.Visibility = _viewModel.IsSeekable ? Visibility.Visible : Visibility.Collapsed;
        SkipBackButton.IsEnabled = _viewModel.IsSeekable;
        SkipForwardButton.IsEnabled = _viewModel.IsSeekable;
    }

    // ── Visualizer (Phase 6) ──────────────────────────────────────────────

    private async void ToggleVisualizerButton_Click(object sender, RoutedEventArgs e)
    {
        // The visualizer only supports audio-only media; ignore the toggle for a video session.
        if (!IsVisualizerAvailable())
        {
            RefreshVisualizerAvailability();
            return;
        }

        _visualizerEnabled = !_visualizerEnabled;

        UpdateVisualizerUi();

        // Start/stop the audio source (ADR-0009). On Windows the source is the WASAPI device-loopback
        // capture: enabling starts capturing the audio the output endpoint is playing (speakers keep
        // playing); disabling stops it. The engine's PCM tap is never used here because it silences
        // the speakers.
        await ApplyVisualizerAudioSourceAsync();
    }

    /// <summary>Starts (or stops) the visualizer's audio source to match <see cref="_visualizerEnabled"/>.</summary>
    private async Task ApplyVisualizerAudioSourceAsync()
    {
        if (_visualizerSource == null)
        {
            return;
        }

        try
        {
            if (_visualizerEnabled)
            {
                // ADR-0009: the WASAPI device-loopback source captures the audio the output endpoint is
                // playing (speakers keep playing). Start() fire-and-forgets the async recorder build.
                _visualizerSource.Start();
            }
            else
            {
                _visualizerSource.Stop();
            }
        }
        catch (Exception ex)
        {
            // A failed start/stop must not break the player; visualizer may show blank.
            System.Diagnostics.Debug.WriteLine($"Visualizer audio source {(_visualizerEnabled ? "start" : "stop")} failed: {ex}");
        }
    }

    // ── Fullscreen Visualizer (VIS-10) ─────────────────────────────────────

    /// <summary>
    /// The visualizer is only meaningful when the loaded media is audio-only (no video tracks). For a
    /// video session the animation would just overlay the picture, so it is disabled entirely — the
    /// same <c>Metadata.VideoTracks.Count == 0</c> rule the engine uses to identify an audio-only
    /// session (and skip the renderer rebuild). Unloaded (null VM) is NOT available.
    /// </summary>
    private bool IsVisualizerAvailable()
        => _viewModel is not null && _viewModel.VideoTracks.Count == 0;

    /// <summary>
    /// Keeps the visualizer's UI surface and menu/button state in sync with whether the current
    /// session is audio-only. When the loaded media gains video tracks (or unloads), any active
    /// visualizer session is torn down so the picture is never overlapped by the animation.
    /// </summary>
    private void RefreshVisualizerAvailability()
    {
        var available = IsVisualizerAvailable();

        var controlsEnabled = available && _viewModel != null;
        if (VisualizerMenuItem != null) VisualizerMenuItem.IsEnabled = controlsEnabled;
        if (VisualizerPresetSubMenu != null) VisualizerPresetSubMenu.IsEnabled = controlsEnabled;
        if (VisualizerPresetSpectrumFlow != null) VisualizerPresetSpectrumFlow.IsEnabled = controlsEnabled;
        if (VisualizerPresetNeonWave != null) VisualizerPresetNeonWave.IsEnabled = controlsEnabled;
        if (VisualizerPresetOrbit != null) VisualizerPresetOrbit.IsEnabled = controlsEnabled;
        if (VisualizerPresetPulseRings != null) VisualizerPresetPulseRings.IsEnabled = controlsEnabled;
        if (VisualizerPresetParticleBloom != null) VisualizerPresetParticleBloom.IsEnabled = controlsEnabled;
        if (VisualizerPresetAuroraRibbon != null) VisualizerPresetAuroraRibbon.IsEnabled = controlsEnabled;
        if (VisualizerPresetPrismTunnel != null) VisualizerPresetPrismTunnel.IsEnabled = controlsEnabled;
        if (VisualizerPresetAlbumPulse != null) VisualizerPresetAlbumPulse.IsEnabled = controlsEnabled;
        if (VisualizerPresetMinimalSpectrum != null) VisualizerPresetMinimalSpectrum.IsEnabled = controlsEnabled;

        // If a visualizer session is active but the media is no longer audio-only (e.g. next-queue
        // advanced to a video), tear it down: disable the overlay and stop the audio source so the
        // picture is restored and no capture is left running.
        if (!available && _visualizerEnabled)
        {
            _visualizerEnabled = false;
            _ = ApplyVisualizerAudioSourceAsync(); // stops the source (best-effort)
        }

        // Always reflect the current availability (e.g. a prior teardown above already called this,
        // but re-running is idempotent).
        UpdateVisualizerUi();
    }

    private void UpdateVisualizerUi()
    {
        var playing = _viewModel?.IsPlaying == true;
        VisualizerSurface.Visibility = (_visualizerEnabled && IsVisualizerAvailable()) ? Visibility.Visible : Visibility.Collapsed;
        VisualizerSurface.IsPaused = !_visualizerEnabled || !IsVisualizerAvailable() || !playing;

        // Lazily bring up the Win2D surface only once the visualizer is actually shown (and, as the
        // overlay pauses on unload, the Draw loop stops). All creation faults are contained.
        if (_visualizerEnabled)
        {
            VisualizerSurface.EnsureReady();
        }

        // The picture always stays visible during normal playback.
        VideoSurface.Visibility = Visibility.Visible;
        UpdateVideoPlaceholderUi();
    }

    /// <summary>
    /// Shows the empty-surface placeholder (video icon + shortcut hint) only until a real video
    /// surface is bound/rendering. When a video is actually attached and playing, the placeholder
    /// must stay hidden — it must never overlay the rendered picture.
    /// </summary>
    private void UpdateVideoPlaceholderUi()
    {
        if (VideoPlaceholderPanel == null) return;
        VideoPlaceholderPanel.Visibility =
            _videoSurfaceAttached
                ? Visibility.Collapsed
                : Visibility.Visible;
    }

    /// <summary>
    /// Appends a handled playback failure to the app's crash log so real-Playback errors can be
    /// diagnosed without an interactive session. Best-effort, never throws. The canonical location
    /// mirrors <c>App.LogCrash</c> (the crash logger in <c>PlaySphere.App</c>).
    /// </summary>
    private static void LogPlaybackError(string message)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LiVi PlaySphere Studio");
            Directory.CreateDirectory(dir);
            File.AppendAllText(
                Path.Combine(dir, "crash.log"),
                $"[{DateTime.UtcNow:o}] PlaybackError: {message}\n");
        }
        catch
        {
            // Logging failures must never take the player down.
        }
    }

    private static IRenderPreset CreatePreset(string name) => name switch
    {
        "NeonWave" => new NeonWavePreset(),
        "OrbitSpectrum" => new OrbitSpectrumPreset(),
        "PulseRings" => new PulseRingsPreset(),
        "ParticleBloom" => new ParticleBloomPreset(),
        "AuroraRibbon" => new AuroraRibbonPreset(),
        "PrismTunnel" => new PrismTunnelPreset(),
        "AlbumPulse" => new AlbumPulsePreset(),
        "MinimalSpectrum" => new MinimalSpectrumPreset(),
        _ => new SpectrumFlowPreset(),
    };

    private void VisualizerPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item || item.Tag is not string tag)
        {
            return;
        }

        VisualizerSurface.Preset = CreatePreset(tag);
    }

    // ── Side Drawer System (PS-207) ─────────────────────────────────────────

    private PlayerDrawerKind _lastRenderedDrawer = PlayerDrawerKind.None;

    private void UpdateDrawerUi()
    {
        if (_viewModel == null) return;

        var drawer = _viewModel.ActiveDrawer;

        // A drawer's dynamic content (bookmark DB queries, track lists, etc.) is populated only when
        // the drawer kind actually changes. During playback UpdateDrawerUi runs continuously (on
        // every position/property tick), and re-running the Chapters/bookmark DB query + list rebuild
        // every frame floods the UI thread — the video keeps playing on its own native threads while
        // the controls freeze (the "Chapters & Bookmarks opens and the player controls freeze" bug).
        var drawerChanged = drawer != _lastRenderedDrawer;
        _lastRenderedDrawer = drawer;

        if (drawer == PlayerDrawerKind.None)
        {
            DrawerOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        DrawerOverlay.Visibility = Visibility.Visible;
        AudioTracksPanel.Visibility = Visibility.Collapsed;
        SubtitleTracksPanel.Visibility = Visibility.Collapsed;
        PlaybackSpeedPanel.Visibility = Visibility.Collapsed;
        MediaInfoPanel.Visibility = Visibility.Collapsed;
        ChaptersPanel.Visibility = Visibility.Collapsed;
        EqualizerPanel.Visibility = Visibility.Collapsed;

        switch (drawer)
        {
            case PlayerDrawerKind.AudioTracks:
                DrawerTitle.Text = "Audio Tracks";
                AudioTracksPanel.Visibility = Visibility.Visible;
                if (drawerChanged) PopulateAudioTracks();
                break;

            case PlayerDrawerKind.SubtitleTracks:
                DrawerTitle.Text = "Subtitles";
                SubtitleTracksPanel.Visibility = Visibility.Visible;
                if (drawerChanged) PopulateSubtitleTracks();
                break;

            case PlayerDrawerKind.PlaybackSpeed:
                DrawerTitle.Text = "Playback Speed";
                PlaybackSpeedPanel.Visibility = Visibility.Visible;
                if (drawerChanged) SpeedSlider.Value = _viewModel.Rate;
                break;

            case PlayerDrawerKind.MediaInfo:
                DrawerTitle.Text = "Media Information";
                MediaInfoPanel.Visibility = Visibility.Visible;
                if (drawerChanged) PopulateMediaInfo();
                break;

            case PlayerDrawerKind.Chapters:
            case PlayerDrawerKind.Bookmarks:
                DrawerTitle.Text = "Chapters & Bookmarks";
                ChaptersPanel.Visibility = Visibility.Visible;
                if (_viewModel != null && drawerChanged)
                {
                    AddBookmarkButton.IsEnabled = _viewModel.CanBookmark;
                    GenerateChaptersButton.IsEnabled = _viewModel.CanGenerateChapters;
                    ChaptersStatusText.Text = _viewModel.ChaptersMessage
                        ?? "Generate chapters from this file's transcript to jump between topics.";
                    _ = PopulateBookmarksUiAsync();
                    _ = PopulateChaptersUiAsync();
                }
                break;

            case PlayerDrawerKind.Equalizer:
                DrawerTitle.Text = "Equalizer";
                EqualizerPanel.Visibility = Visibility.Visible;
                if (drawerChanged) PopulateEqualizer();
                break;
        }
    }

    private async Task PopulateBookmarksUiAsync()
    {
        if (_viewModel == null) return;

        await _viewModel.LoadBookmarksAsync();

        BookmarksList.Children.Clear();
        AddBookmarkButton.IsEnabled = _viewModel.CanBookmark;

        var bookmarks = _viewModel.Bookmarks;
        if (bookmarks.Count == 0)
        {
            BookmarksList.Children.Add(new TextBlock
            {
                Text = "No bookmarks yet. Press \"Bookmark Current Position\" to save one.",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)XamlApplication.Current.Resources["BrushTextMuted"],
            });
            return;
        }

        foreach (var bookmark in bookmarks)
        {
            var label = string.IsNullOrWhiteSpace(bookmark.Label)
                ? FormatBookmarkPosition(bookmark.PositionMs)
                : $"{bookmark.Label} · {FormatBookmarkPosition(bookmark.PositionMs)}";

            var card = new Grid
            {
                CornerRadius = new CornerRadius(8),
                Background = (Microsoft.UI.Xaml.Media.Brush)XamlApplication.Current.Resources["BrushSurfaceCard"],
                Padding = new Thickness(10, 6, 6, 6),
                ColumnSpacing = 6,
            };
            card.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            card.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var text = new TextBlock
            {
                Text = label,
                FontSize = 12,
                TextTrimming = Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)XamlApplication.Current.Resources["BrushTextPrimary"],
            };
            card.Children.Add(text);

            var remove = new Button
            {
                Content = new FontIcon { Glyph = '\uE74D'.ToString(), FontSize = 12 },
                FontSize = 11,
                Padding = new Thickness(6),
                VerticalAlignment = VerticalAlignment.Center,
                Background = (Microsoft.UI.Xaml.Media.Brush)XamlApplication.Current.Resources["BrushSurfaceHover"],
            };
            ToolTipService.SetToolTip(remove, "Remove bookmark");
            remove.Click += async (_, _) =>
            {
                await _viewModel.RemoveBookmarkAsync(bookmark.Id);
                await PopulateBookmarksUiAsync();
            };
            Grid.SetColumn(remove, 1);
            card.Children.Add(remove);

            card.Tapped += async (_, _) => await _viewModel.SeekToBookmarkAsync(bookmark);

            BookmarksList.Children.Add(card);
        }
    }

    private void UpdateQueueTransportUi()
    {
        if (_viewModel == null) return;

        var active = _viewModel.IsQueueActive;
        PlayPreviousButton.IsEnabled = active && _viewModel.HasPrevious;
        PlayNextButton.IsEnabled = active && _viewModel.HasNext;
        ShuffleButton.IsEnabled = active;
        RepeatButton.IsEnabled = active;

        if (!active) return;

        ToolTipService.SetToolTip(ShuffleButton, _viewModel.IsShuffled ? "Shuffle: On" : "Shuffle");
        ShuffleButton.Foreground = _viewModel.IsShuffled
            ? (Microsoft.UI.Xaml.Media.Brush)XamlApplication.Current.Resources["BrushAccentCyan"]
            : (Microsoft.UI.Xaml.Media.Brush)XamlApplication.Current.Resources["BrushTextSecondary"];

        var (repeatGlyph, repeatTooltip) = _viewModel.RepeatMode switch
        {
            PlaySphere.Domain.Playback.PlaybackRepeat.One => ('\uE8ED', "Repeat: One"),
            PlaySphere.Domain.Playback.PlaybackRepeat.All => ('\uE735', "Repeat: All"),
            _ => ('\uE8EE', "Repeat: Off"),
        };
        RepeatButton.Content = repeatGlyph.ToString();
        ToolTipService.SetToolTip(RepeatButton, repeatTooltip);
        RepeatButton.Foreground = _viewModel.RepeatMode == PlaySphere.Domain.Playback.PlaybackRepeat.Off
            ? (Microsoft.UI.Xaml.Media.Brush)XamlApplication.Current.Resources["BrushTextSecondary"]
            : (Microsoft.UI.Xaml.Media.Brush)XamlApplication.Current.Resources["BrushAccentCyan"];
    }

    private static string FormatBookmarkPosition(long positionMs)
    {
        var t = TimeSpan.FromMilliseconds(Math.Max(0, positionMs));
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }

    private async Task PopulateChaptersUiAsync()
    {
        if (_viewModel == null) return;

        await _viewModel.LoadChaptersAsync();

        ChaptersList.Children.Clear();
        GenerateChaptersButton.IsEnabled = _viewModel.CanGenerateChapters;
        ChaptersStatusText.Text = _viewModel.ChaptersMessage
            ?? (_viewModel.Chapters.Count > 0
                ? "Auto-generated chapters from this file's transcript."
                : "No AI chapters yet. Transcribe the file (AI Media) then press \u201cGenerate AI Chapters\u201d.");

        var chapters = _viewModel.Chapters;
        if (chapters.Count == 0)
        {
            ChaptersList.Children.Add(new TextBlock
            {
                Text = "No AI chapters generated yet.",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)XamlApplication.Current.Resources["BrushTextMuted"],
            });
            return;
        }

        foreach (var chapter in chapters)
        {
            var title = string.IsNullOrWhiteSpace(chapter.Title) ? "Chapter" : chapter.Title;
            var card = new Grid
            {
                CornerRadius = new CornerRadius(8),
                Background = (Microsoft.UI.Xaml.Media.Brush)XamlApplication.Current.Resources["BrushSurfaceCard"],
                Padding = new Thickness(10, 6, 6, 6),
                ColumnSpacing = 8,
            };
            card.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            card.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var text = new TextBlock
            {
                Text = $"{FormatBookmarkPosition((long)(chapter.StartSeconds * 1000))}  {title}",
                FontSize = 12,
                TextTrimming = Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)XamlApplication.Current.Resources["BrushTextPrimary"],
            };
            card.Children.Add(text);

            var glyph = new FontIcon { Glyph = '\uE768'.ToString(), FontSize = 12 };
            glyph.Foreground = (Microsoft.UI.Xaml.Media.Brush)XamlApplication.Current.Resources["BrushAccentCyan"];
            glyph.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(glyph, 1);
            card.Children.Add(glyph);

            ToolTipService.SetToolTip(card, "Seek to this chapter");
            card.Tapped += async (_, _) => await _viewModel.SeekToChapterAsync(chapter);
            ChaptersList.Children.Add(card);
        }
    }

    private void PopulateAudioTracks()
    {
        AudioTracksList.Children.Clear();
        if (_viewModel == null) return;

        AudioDelaySlider.Value = _viewModel.AudioDelayMilliseconds;
        AudioDelayValueText.Text = _viewModel.AudioDelayText;

        var tracks = _viewModel.AudioTracks;
        if (tracks.Count == 0)
        {
            AudioTracksList.Children.Add(new TextBlock
            {
                Text = "No audio streams detected.",
                FontSize = 13,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)XamlApplication.Current.Resources["BrushTextMuted"]
            });
            return;
        }

        foreach (var track in tracks)
        {
            var label = $"Track {track.Id}: {(track.Name ?? track.Language ?? "Stream")} ({track.Codec ?? "Audio"}, {track.Channels ?? 2}ch)";
            var rb = new RadioButton
            {
                Content = label,
                Tag = track.Id,
                IsChecked = _viewModel.SelectedAudioTrackId == track.Id || (_viewModel.SelectedAudioTrackId == null && track.Id == tracks[0].Id),
                GroupName = "AudioTracksGroup"
            };
            rb.Checked += async (_, _) =>
            {
                if (rb.Tag is int trackId)
                {
                    await _viewModel.SelectAudioTrackAsync(trackId);
                }
            };
            AudioTracksList.Children.Add(rb);
        }

        PopulateAudioOutputAndEnhancements();
    }

    private void PopulateAudioOutputAndEnhancements()
    {
        if (_viewModel == null) return;

        _isPopulatingDrawer = true;
        try
        {
            AudioOutputDeviceList.Children.Clear();
            var devices = _viewModel.AudioOutputDevices;
            if (devices.Count == 0)
            {
                AudioOutputDeviceList.Children.Add(new TextBlock
                {
                    Text = "No enumerable output devices; using system default.",
                    FontSize = 12,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)XamlApplication.Current.Resources["BrushTextMuted"]
                });
            }
            else
            {
                foreach (var device in devices)
                {
                    var rb = new RadioButton
                    {
                        Content = device.Name,
                        Tag = device.Id,
                        IsChecked = _viewModel.ActiveAudioOutputDeviceId == device.Id,
                        GroupName = "AudioOutputDeviceGroup"
                    };
                    rb.Checked += async (_, _) =>
                    {
                        if (!_isPopulatingDrawer && rb.Tag is string deviceId && _viewModel != null)
                        {
                            await _viewModel.SetAudioOutputDeviceAsync(deviceId);
                        }
                    };
                    AudioOutputDeviceList.Children.Add(rb);
                }
            }

            SelectComboBoxItemByTag(AudioChannelComboBox, _viewModel.AudioChannel.ToString());
            LoudnessNormalizationToggle.IsOn = _viewModel.IsLoudnessNormalizationEnabled;
            NightModeToggle.IsOn = _viewModel.IsNightModeEnabled;

            StereoBalanceSection.Visibility = _viewModel.CanUseStereoBalance ? Visibility.Visible : Visibility.Collapsed;
            StereoBalanceSlider.Value = _viewModel.StereoBalanceValue;
            StereoBalanceValueText.Text = FormatStereoBalance(_viewModel.StereoBalanceValue);
        }
        finally
        {
            _isPopulatingDrawer = false;
        }
    }

    private static string FormatStereoBalance(int value) => value switch
    {
        0 => "Center",
        < 0 => $"{-value}% Left",
        _ => $"{value}% Right",
    };

    private static void SelectComboBoxItemByTag(ComboBox comboBox, string tag)
    {
        foreach (var item in comboBox.Items)
        {
            if (item is ComboBoxItem { Tag: string itemTag } cbi && itemTag == tag)
            {
                comboBox.SelectedItem = cbi;
                return;
            }
        }
    }

    private async void AudioChannelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isPopulatingDrawer || _viewModel == null) return;
        if (AudioChannelComboBox.SelectedItem is ComboBoxItem { Tag: string tag } &&
            Enum.TryParse<AudioChannelMode>(tag, out var mode))
        {
            await _viewModel.SetAudioChannelAsync(mode);
        }
    }

    /// <summary>
    /// Debounces the stereo balance slider (ADR-0011): each value change restarts the secondary
    /// ffmpeg decode segment, so applying on every drag tick would spawn overlapping processes.
    /// Commits ~300ms after the user stops moving the slider; the label updates live regardless.
    /// </summary>
    private void StereoBalanceSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isPopulatingDrawer || _viewModel == null) return;

        _pendingStereoBalanceValue = (int)Math.Round(e.NewValue);
        StereoBalanceValueText.Text = FormatStereoBalance(_pendingStereoBalanceValue);

        _stereoBalanceDebounceTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _stereoBalanceDebounceTimer.Stop();
        _stereoBalanceDebounceTimer.Tick -= StereoBalanceDebounceTimer_Tick;
        _stereoBalanceDebounceTimer.Tick += StereoBalanceDebounceTimer_Tick;
        _stereoBalanceDebounceTimer.Start();
    }

    private async void StereoBalanceDebounceTimer_Tick(object? sender, object e)
    {
        _stereoBalanceDebounceTimer?.Stop();
        if (_viewModel == null) return;
        await _viewModel.SetStereoBalanceAsync(_pendingStereoBalanceValue);
    }

    private async void LoudnessNormalizationToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isPopulatingDrawer || _viewModel == null) return;
        await _viewModel.SetLoudnessNormalizationEnabledAsync(LoudnessNormalizationToggle.IsOn);
    }

    private async void NightModeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isPopulatingDrawer || _viewModel == null) return;
        await _viewModel.SetNightModeEnabledAsync(NightModeToggle.IsOn);
    }

    private void PopulateSubtitleTracks()
    {
        SubtitleTracksList.Children.Clear();
        if (_viewModel == null) return;

        SubtitleDelaySlider.Value = _viewModel.SubtitleDelayMilliseconds;
        SubtitleDelayValueText.Text = _viewModel.SubtitleDelayText;

        var tracks = _viewModel.SubtitleTracks;

        // "Off" option
        var offRb = new RadioButton
        {
            Content = "Off (Disabled)",
            Tag = (int?)null,
            IsChecked = _viewModel.SelectedSubtitleTrackId == null,
            GroupName = "SubtitleTracksGroup"
        };
        offRb.Checked += async (_, _) => await _viewModel.SelectSubtitleTrackAsync(null);
        SubtitleTracksList.Children.Add(offRb);

        foreach (var track in tracks)
        {
            var label = $"Track {track.Id}: {(track.Name ?? track.Language ?? "Subtitle")} ({track.Encoding ?? "SRT/Text"})";
            var rb = new RadioButton
            {
                Content = label,
                Tag = track.Id,
                IsChecked = _viewModel.SelectedSubtitleTrackId == track.Id,
                GroupName = "SubtitleTracksGroup"
            };
            rb.Checked += async (_, _) =>
            {
                if (rb.Tag is int trackId)
                {
                    await _viewModel.SelectSubtitleTrackAsync(trackId);
                }
            };
            SubtitleTracksList.Children.Add(rb);
        }

        _isPopulatingDrawer = true;
        try
        {
            SubtitleTextScaleSlider.Value = _viewModel.SubtitleTextScalePercent;
            SubtitleTextScaleValueText.Text = $"{_viewModel.SubtitleTextScalePercent}%";

            PopulateSubtitleStyleControls();
        }
        finally
        {
            _isPopulatingDrawer = false;
        }

        var hasDocument = _viewModel.LoadedSubtitleDocument != null;
        SubtitleCueSectionHint.Visibility = hasDocument ? Visibility.Collapsed : Visibility.Visible;
        SubtitleCueSearchControls.Visibility = hasDocument ? Visibility.Visible : Visibility.Collapsed;
        SubtitleCueSearchBox.Text = string.Empty;
        SubtitleCueResultsList.Children.Clear();
    }

    private async void SubtitleTextScaleSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        SubtitleTextScaleValueText.Text = $"{(int)e.NewValue}%";
        if (_isPopulatingDrawer || _viewModel == null) return;
        await _viewModel.SetSubtitleTextScalePercentAsync((int)e.NewValue);
    }

    private static readonly (string Name, int? Argb)[] SubtitleColorPresets =
    {
        ("Default", null),
        ("White", 0xFFFFFF),
        ("Yellow", 0xFFFF00),
        ("Red", 0xFF0000),
        ("Green", 0x00FF00),
        ("Cyan", 0x00FFFF),
        ("Blue", 0x0000FF),
        ("Black", 0x000000),
    };

    private static readonly string[] SubtitleFontPresets =
    {
        "Default", "Segoe UI", "Arial", "Calibri", "Times New Roman", "Consolas", "Georgia", "Verdana", "Tahoma",
    };

    private void PopulateSubtitleStyleControls()
    {
        if (_viewModel == null) return;

        SubtitleFontComboBox.Items.Clear();
        foreach (var font in SubtitleFontPresets)
        {
            SubtitleFontComboBox.Items.Add(font);
        }
        SubtitleFontComboBox.SelectedItem = string.IsNullOrWhiteSpace(_viewModel.SubtitleFontFamily)
            ? "Default"
            : _viewModel.SubtitleFontFamily;

        PopulateColorCombo(SubtitleTextColorComboBox, _viewModel.SubtitleTextColorArgb, SubtitleColorPresets);
        PopulateColorCombo(SubtitleOutlineColorComboBox, _viewModel.SubtitleOutlineColorArgb, SubtitleColorPresets);

        SubtitleOutlineComboBox.Items.Clear();
        SubtitleOutlineComboBox.Items.Add(new ComboBoxItem { Content = "Off", Tag = (int)SubtitleOutlineStyle.Off });
        SubtitleOutlineComboBox.Items.Add(new ComboBoxItem { Content = "Thin", Tag = (int)SubtitleOutlineStyle.Thin });
        SubtitleOutlineComboBox.Items.Add(new ComboBoxItem { Content = "Normal", Tag = (int)SubtitleOutlineStyle.Normal });
        SelectOutline(_viewModel.SubtitleOutline);

        SubtitleMarginSlider.Value = _viewModel.SubtitleVerticalMarginPx;
        SubtitleMarginValueText.Text = MarginText(_viewModel.SubtitleVerticalMarginPx);
    }

    private static void PopulateColorCombo(ComboBox combo, int? current, (string Name, int? Argb)[] presets)
    {
        combo.Items.Clear();
        foreach (var (name, argb) in presets)
        {
            combo.Items.Add(new ComboBoxItem { Content = name, Tag = argb });
        }
        SelectColor(combo, current);
    }

    private static void SelectColor(ComboBox combo, int? current)
    {
        for (int i = 0; i < combo.Items.Count; i++)
        {
            var item = (ComboBoxItem)combo.Items[i];
            int? tag = item.Tag as int? ?? (item.Tag is int v ? v : (int?)null);
            if (tag == current)
            {
                combo.SelectedIndex = i;
                return;
            }
        }
        combo.SelectedIndex = 0;
    }

    private void SelectOutline(SubtitleOutlineStyle outline)
    {
        for (int i = 0; i < SubtitleOutlineComboBox.Items.Count; i++)
        {
            var item = (ComboBoxItem)SubtitleOutlineComboBox.Items[i];
            if ((int)item.Tag == (int)outline)
            {
                SubtitleOutlineComboBox.SelectedIndex = i;
                return;
            }
        }
        SubtitleOutlineComboBox.SelectedIndex = 0;
    }

    private static string MarginText(int marginPx) => marginPx == 0 ? "Bottom" : $"{marginPx}px up";

    private async void SubtitleFontComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isPopulatingDrawer || _viewModel == null) return;
        var selected = SubtitleFontComboBox.SelectedItem as string;
        await _viewModel.SetSubtitleFontAsync(selected == "Default" ? null : selected);
    }

    private async void SubtitleTextColorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isPopulatingDrawer || _viewModel == null || SubtitleTextColorComboBox.SelectedItem is not ComboBoxItem colorItem) return;
        await _viewModel.SetSubtitleColorAsync(colorItem.Tag as int? ?? (colorItem.Tag is int v ? v : (int?)null));
    }

    private async void SubtitleOutlineColorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isPopulatingDrawer || _viewModel == null || SubtitleOutlineColorComboBox.SelectedItem is not ComboBoxItem colorItem) return;
        await _viewModel.SetSubtitleOutlineColorAsync(colorItem.Tag as int? ?? (colorItem.Tag is int v ? v : (int?)null));
    }

    private async void SubtitleOutlineComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isPopulatingDrawer || _viewModel == null || SubtitleOutlineComboBox.SelectedItem is not ComboBoxItem outlineItem) return;
        await _viewModel.SetSubtitleOutlineAsync((SubtitleOutlineStyle)(int)outlineItem.Tag);
    }

    private async void SubtitleMarginSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        SubtitleMarginValueText.Text = MarginText((int)e.NewValue);
        if (_isPopulatingDrawer || _viewModel == null) return;
        await _viewModel.SetSubtitleVerticalMarginAsync((int)e.NewValue);
    }

    private void SubtitleCueSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SubtitleCueResultsList.Children.Clear();
        if (_viewModel == null) return;

        var query = SubtitleCueSearchBox.Text;
        if (string.IsNullOrWhiteSpace(query)) return;

        foreach (var cue in _viewModel.SearchSubtitleCues(query).Take(50))
        {
            var snippet = cue.Text.Replace('\n', ' ');
            if (snippet.Length > 80) snippet = snippet[..80] + "…";

            var button = new Button
            {
                Content = $"{cue.Start:mm\\:ss}  {snippet}",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = (Microsoft.UI.Xaml.Media.Brush)XamlApplication.Current.Resources["BrushSurfaceCard"],
                BorderThickness = new Thickness(0),
                FontSize = 12,
                Tag = cue
            };
            button.Click += async (_, _) =>
            {
                if (button.Tag is SubtitleCue clickedCue && _viewModel != null)
                {
                    await _viewModel.SeekToCueAsync(clickedCue);
                }
            };
            SubtitleCueResultsList.Children.Add(button);
        }
    }

    private async void ExportSubtitleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;

        try
        {
            var suggestedName = string.IsNullOrEmpty(_viewModel.Title) ? "subtitle" : _viewModel.Title;
            var savePath = await FilePickerHelper.PickSaveSubtitleFileAsync(suggestedName);
            if (!string.IsNullOrEmpty(savePath))
            {
                await _viewModel.ExportLoadedSubtitleAsync(savePath);
            }
        }
        catch (Exception)
        {
            // Failures gracefully handled without crashing UI
        }
    }

    private void PopulateMediaInfo()
    {
        if (_viewModel == null) return;

        InfoFileName.Text = $"File: {System.IO.Path.GetFileName(_viewModel.FilePath) ?? _viewModel.Title}";
        InfoContainer.Text = $"Container: {_viewModel.Metadata.Container ?? "Auto-detected"}";
        InfoDuration.Text = $"Duration: {_viewModel.DurationText}";

        var libItem = _viewModel.LibraryItem;
        var libBitrate = libItem?.BitrateKbps is > 0 ? $"{libItem!.BitrateKbps.Value} Kbps" : null;
        var libFrameRate = libItem?.FrameRate is > 0 ? $"{libItem!.FrameRate.Value:0.##} fps" : null;
        var libTechnical = libBitrate != null || libFrameRate != null
            ? $"Bitrate: {libBitrate ?? "Unknown"}\nFrame rate: {libFrameRate ?? "Unknown"}"
            : null;

        var video = _viewModel.VideoTracks.FirstOrDefault();
        var resolution = video?.Resolution ?? libItem?.Resolution ?? "Auto";
        var codec = video?.Codec ?? libItem?.Codec ?? "Unknown";
        InfoVideoDetails.Text = video != null || (libItem?.Resolution ?? libItem?.Codec) != null
            ? $"Codec: {codec}\nResolution: {resolution}" + (libTechnical is null ? "" : $"\n{libTechnical}")
            : "No video track";

        var audioList = _viewModel.AudioTracks;
        InfoAudioDetails.Text = audioList.Count > 0
            ? string.Join("\n", audioList.Select(a => $"Stream {a.Id}: {a.Codec} ({a.SampleRate} Hz, {a.Channels}ch)"))
            : (libItem?.AudioTrackCount > 0
                ? $"Streams: {libItem.AudioTrackCount} audio"
                : "No audio tracks");
    }

    private void PopulateEqualizer()
    {
        if (_viewModel == null) return;

        _isPopulatingDrawer = true;
        try
        {
            var config = _viewModel.EqualizerConfig;

            EqualizerEnabledToggle.IsOn = _viewModel.IsEqualizerEnabled;

            EqualizerPresetComboBox.ItemsSource = _viewModel.EqualizerPresetNames;
            EqualizerPresetComboBox.SelectedItem = config.PresetName;

            EqualizerPreampSlider.Value = config.PreampGainDb;
            EqualizerPreampValueText.Text = $"{config.PreampGainDb:F1} dB";

            EqualizerBandsList.Children.Clear();
            for (var i = 0; i < config.BandGainsDb.Count; i++)
            {
                var bandIndex = (uint)i;
                var frequencyHz = i < EqualizerPresets.StandardFrequencies.Count ? EqualizerPresets.StandardFrequencies[i] : 0f;
                var label = frequencyHz >= 1000f ? $"{frequencyHz / 1000f:0.#}kHz" : $"{frequencyHz:0}Hz";

                var row = new Grid { ColumnSpacing = 10 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });

                var labelText = new TextBlock
                {
                    Text = label,
                    FontSize = 11,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)XamlApplication.Current.Resources["BrushTextMuted"],
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(labelText, 0);

                var valueText = new TextBlock
                {
                    Text = $"{config.BandGainsDb[i]:F1}",
                    FontSize = 11,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)XamlApplication.Current.Resources["BrushAccentBlue"],
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                Grid.SetColumn(valueText, 2);

                var slider = new Slider
                {
                    Minimum = -20,
                    Maximum = 20,
                    StepFrequency = 0.5,
                    Value = config.BandGainsDb[i],
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(slider, 1);
                slider.ValueChanged += async (_, args) =>
                {
                    valueText.Text = $"{args.NewValue:F1}";
                    if (_isPopulatingDrawer || _viewModel == null) return;
                    await _viewModel.SetEqualizerBandGainAsync(bandIndex, (float)args.NewValue);
                };

                row.Children.Add(labelText);
                row.Children.Add(slider);
                row.Children.Add(valueText);
                EqualizerBandsList.Children.Add(row);
            }
        }
        finally
        {
            _isPopulatingDrawer = false;
        }
    }

    private async void EqualizerEnabledToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isPopulatingDrawer || _viewModel == null) return;
        await _viewModel.SetEqualizerEnabledAsync(EqualizerEnabledToggle.IsOn);
    }

    private async void EqualizerPresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isPopulatingDrawer || _viewModel == null) return;
        if (EqualizerPresetComboBox.SelectedItem is string presetName)
        {
            await _viewModel.SetEqualizerPresetAsync(presetName);
            PopulateEqualizer();
        }
    }

    private async void EqualizerPreampSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        EqualizerPreampValueText.Text = $"{e.NewValue:F1} dB";
        if (_isPopulatingDrawer || _viewModel == null) return;
        await _viewModel.SetEqualizerPreampAsync((float)e.NewValue);
    }

    private async void ResetEqualizerButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        await _viewModel.ResetEqualizerAsync();
        PopulateEqualizer();
    }

    private void EqualizerButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.ToggleDrawer(PlayerDrawerKind.Equalizer);
    }

    private void MediaInfoButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.ToggleDrawer(PlayerDrawerKind.MediaInfo);
    }

    /// <summary>
    /// Phase 7 (PS-701): captures the current video frame as a PNG via a save picker, then reports
    /// the outcome in a dialog. The engine snapshot is best-effort (never disturbs playback); the
    /// handler only surfaces the result.
    /// </summary>
    private async void SaveSnapshotButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;

        if (!_viewModel.CanCaptureSnapshot)
        {
            await ShowSnapshotResultDialogAsync("There is no video picture to capture right now.");
            return;
        }

        string? path = null;
        try
        {
            var suggested = SnapshotNaming.SuggestFileName(_viewModel.Title);
            path = await FilePickerHelper.PickSaveSnapshotFileAsync(suggested);
            if (string.IsNullOrEmpty(path)) return;

            var ok = await _viewModel.SaveSnapshotAsync(path);
            await ShowSnapshotResultDialogAsync(
                ok ? $"Snapshot saved to:\n{path}" : "Capture failed — no frame could be captured at the current position.");
        }
        catch (Exception ex)
        {
            await ShowSnapshotResultDialogAsync($"Snapshot failed: {ex.Message}");
        }
    }

    private Task ShowSnapshotResultDialogAsync(string message) => ShowInfoDialogAsync("Save Snapshot", message);

    /// <summary>
    /// Shows a simple OK-dismiss informational dialog. WinUI allows only one ContentDialog open
    /// per XamlRoot at a time; showing a second one throws (this crashed the app when a result
    /// dialog was triggered while another was still closing — see AGENTS.md). Losing one
    /// informational message to a rare race is far better than an unhandled crash, so the show
    /// is best-effort.
    /// </summary>
    private async Task ShowInfoDialogAsync(string title, string message)
    {
        if (XamlRoot is null) return;
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = XamlRoot
        };
        try
        {
            await dialog.ShowAsync();
        }
        catch (Exception)
        {
        }
    }

    // ── Phase 7: Mark In / Out + clip export ─────────────────────────────────

    private void MarkInButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.SetMarkIn();
    }

    private void MarkOutButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.SetMarkOut();
    }

    private void ClearMarksButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.ClearMarks();
        UpdateMarkUi();
    }

    private async void ExportClipButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.CanExportClip != true) return;

        string? path = null;
        try
        {
            path = await FilePickerHelper.PickSaveClipFileAsync(SuggestClipFileName());
            if (string.IsNullOrEmpty(path)) return;

            // Prefer the background queue when present; fall back to the foreground VM path.
            var queue = PlaySphereServices.GetOptional<PlaySphere.Application.Media.IConversionQueue>();
            if (queue is not null &&
                _viewModel.MarkIn is { } inPoint && _viewModel.MarkOut is { } outPoint && outPoint > inPoint)
            {
                var enqueued = queue.EnqueueClip(_viewModel.FilePath ?? string.Empty, path, inPoint, outPoint - inPoint);
                await ShowClipExportResultDialogAsync(
                    enqueued
                        ? $"Added to the background conversion queue:\n{path}\n\nTrack progress, cancel, retry or open the output folder in the Conversions page."
                        : "Could not add the clip export — the input file may have been deleted.");
                return;
            }

            var result = await _viewModel.ExportClipAsync(path);
            await ShowClipExportResultDialogAsync(
                result.Success ? $"Clip saved to:\n{path}" :
                $"Clip export failed: {result.ErrorMessage ?? "unknown error"}");
        }
        catch (Exception ex)
        {
            await ShowClipExportResultDialogAsync($"Clip export failed: {ex.Message}");
        }
    }

    private Task ShowClipExportResultDialogAsync(string message) => ShowInfoDialogAsync("Export Clip", message);

    /// <summary>Derives a safe suggested clip file stem from the loaded media title.</summary>
    private string SuggestClipFileName()
    {
        var title = SnapshotNaming.Sanitize(_viewModel?.Title).Replace(' ', '-');
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        return string.IsNullOrWhiteSpace(title) ? $"PlaySphere-clip-{stamp}" : $"{title}-clip-{stamp}";
    }

    /// <summary>
    /// Enables the Clear Marks / Export Clip overflow items from the VM mark state, and positions
    /// the In→Out range overlay under the timeline. Cheap — runs on every VM property tick.
    /// </summary>
    private void UpdateMarkUi()
    {
        if (_viewModel == null) return;

        var marked = _viewModel.IsMarked;
        ClearMarksMenuItem.IsEnabled = marked;
        ExportClipMenuItem.IsEnabled = _viewModel.CanExportClip;

        if (!marked || TimelineSlider.ActualWidth <= 0)
        {
            MarkRangeOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        MarkRangeOverlay.Visibility = Visibility.Visible;
        var left = _viewModel.MarkInPercent / 100.0 * TimelineSlider.ActualWidth;
        var right = _viewModel.MarkOutPercent / 100.0 * TimelineSlider.ActualWidth;
        MarkRangeOverlay.Margin = new Thickness(Math.Max(0, left), 0, 0, 0);
        MarkRangeOverlay.Width = Math.Max(0, right - left);
    }

    private void TimelineSlider_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateMarkOverlayPosition();
    }

    private void UpdateMarkOverlayPosition()
    {
        if (_viewModel?.IsMarked != true || TimelineSlider.ActualWidth <= 0)
        {
            return;
        }

        var left = _viewModel.MarkInPercent / 100.0 * TimelineSlider.ActualWidth;
        var right = _viewModel.MarkOutPercent / 100.0 * TimelineSlider.ActualWidth;
        MarkRangeOverlay.Margin = new Thickness(Math.Max(0, left), 0, 0, 0);
        MarkRangeOverlay.Width = Math.Max(0, right - left);
    }

    // ── Phase 7 (PS-705): Timeline waveform ─────────────────────────────────

    /// <summary>
    /// Shows the peak-level waveform strip behind the timeline slider when the VM has a usable
    /// waveform for the loaded file; otherwise keeps it collapsed. Cheap — runs on every VM tick,
    /// switching sources only when the strip actually changes.
    /// </summary>
    private void UpdateWaveformUi()
    {
        if (_viewModel?.Waveform is { Success: true, ImagePath: not null } waveform)
        {
            if (WaveformImage.Source is not BitmapImage current || !string.Equals(current.UriSource?.LocalPath ?? string.Empty, waveform.ImagePath, StringComparison.OrdinalIgnoreCase))
            {
                WaveformImage.Source = TryCreateBitmap(waveform.ImagePath);
            }

            WaveformImage.Visibility = Visibility.Visible;
        }
        else
        {
            WaveformImage.Source = null;
            WaveformImage.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Tries to create a <see cref="BitmapImage"/> from a (possibly stale) file path; returns null rather than throwing.</summary>
    private static BitmapImage? TryCreateBitmap(string? path)
    {
        try
        {
            return string.IsNullOrWhiteSpace(path) ? null : new BitmapImage(new Uri(path, UriKind.Absolute));
        }
        catch (Exception ex) when (ex is UriFormatException or ArgumentException)
        {
            return null;
        }
    }

    // ── Phase 7 (PS-703): Media conversion ───────────────────────────────────

    private void ConversionsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Frame?.Navigate(typeof(ConversionsPage));
    }

    private async void ConvertButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.CanConvert != true || _viewModel.IsConverting) return;
        if (XamlRoot is null) return;

        ConvertPresetList.ItemsSource = PlaySphere.Domain.Media.ConversionPresets.All;
        ConvertPresetList.SelectedIndex = 0;
        var title = _viewModel.Title ?? "Now Playing";
        ConvertSourceText.Text = $"Convert “{title}” to:";

        ConvertDialog.XamlRoot = XamlRoot;
        ContentDialogResult dialogResult;
        try
        {
            dialogResult = await ConvertDialog.ShowAsync();
        }
        catch (Exception)
        {
            // Another ContentDialog was already open (WinUI allows only one per XamlRoot); treat
            // as cancelled rather than crash — see ShowInfoDialogAsync's note.
            return;
        }
        if (dialogResult != ContentDialogResult.Primary) return;
        if (ConvertPresetList.SelectedItem is not PlaySphere.Domain.Media.ConversionPreset preset) return;

        string? path = null;
        try
        {
            path = await FilePickerHelper.PickSaveConvertFileAsync(
                SuggestConvertFileName(preset), preset.DisplayName, preset.TargetExtension);
            if (string.IsNullOrEmpty(path)) return;

            // Prefer the background queue when present; fall back to the foreground VM path.
            var queue = PlaySphereServices.GetOptional<PlaySphere.Application.Media.IConversionQueue>();
            if (queue is not null)
            {
                var enqueued = queue.EnqueueConvert(_viewModel.FilePath ?? string.Empty, path, preset);
                await ShowConversionResultDialogAsync(
                    enqueued
                        ? $"Added to the background conversion queue:\n{path}\n\nTrack progress, cancel, retry or open the output folder in the Conversions page."
                        : "Could not add the conversion — the input file may have been deleted.");
                return;
            }

            var conversion = await _viewModel.ConvertAsync(preset, path);
            await ShowConversionResultDialogAsync(
                conversion.Success
                    ? $"Conversion finished:\n{path}"
                    : $"Conversion failed: {conversion.ErrorMessage ?? "unknown error"}");
        }
        catch (Exception ex)
        {
            await ShowConversionResultDialogAsync($"Conversion failed: {ex.Message}");
        }
    }

    private Task ShowConversionResultDialogAsync(string message) => ShowInfoDialogAsync("Convert Media", message);

    /// <summary>Derives a safe, preset-suffixed suggested convert file stem from the media title.</summary>
    private string SuggestConvertFileName(PlaySphere.Domain.Media.ConversionPreset preset)
    {
        var title = SnapshotNaming.Sanitize(_viewModel?.Title).Replace(' ', '-');
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var stem = string.IsNullOrWhiteSpace(title) ? $"PlaySphere-converted-{stamp}" : $"{title}-converted-{stamp}";
        return string.Concat(stem, preset.TargetExtension);
    }

    /// <summary>Enables the Convert overflow item from the VM conversion state. Cheap — runs on every tick.</summary>
    private void UpdateConvertUi()
    {
        if (_viewModel == null) return;

        var converting = _viewModel.IsConverting;
        ConvertMenuItem.IsEnabled = !converting && _viewModel.CanConvert;
        ConvertMenuItem.Text = converting ? "Converting…" : "Convert…";
    }

    private void AudioTracksButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.ToggleDrawer(PlayerDrawerKind.AudioTracks);
    }

    private void SubtitlesButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.ToggleDrawer(PlayerDrawerKind.SubtitleTracks);
    }

    private async void AddSubtitleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;

        try
        {
            var subtitlePath = await FilePickerHelper.PickSubtitleFileAsync();
            if (!string.IsNullOrEmpty(subtitlePath))
            {
                await _viewModel.LoadExternalSubtitleAsync(subtitlePath);
                PopulateSubtitleTracks();
            }
        }
        catch (Exception)
        {
            // Failures gracefully handled without crashing UI
        }
    }

    private async void SubtitleDelaySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isUpdatingSliderFromVm || _viewModel == null) return;

        _isDraggingSubtitleSlider = true;
        try
        {
            if (Math.Abs(_viewModel.SubtitleDelayMilliseconds - e.NewValue) > 1)
            {
                await _viewModel.SetSubtitleDelayAsync(TimeSpan.FromMilliseconds(e.NewValue));
            }
        }
        finally
        {
            _isDraggingSubtitleSlider = false;
        }
    }

    private async void SubtitleDelayStep_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && double.TryParse(tag, System.Globalization.CultureInfo.InvariantCulture, out var deltaMs) && _viewModel != null)
        {
            await _viewModel.AdjustSubtitleDelayAsync(TimeSpan.FromMilliseconds(deltaMs));
        }
    }

    private async void ResetSubtitleDelayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
        {
            await _viewModel.ResetSubtitleDelayAsync();
        }
    }

    private async void AudioDelaySlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isUpdatingSliderFromVm || _viewModel == null) return;

        _isDraggingAudioSlider = true;
        try
        {
            if (Math.Abs(_viewModel.AudioDelayMilliseconds - e.NewValue) > 1)
            {
                await _viewModel.SetAudioDelayAsync(TimeSpan.FromMilliseconds(e.NewValue));
            }
        }
        finally
        {
            _isDraggingAudioSlider = false;
        }
    }

    private async void AudioDelayStep_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && double.TryParse(tag, System.Globalization.CultureInfo.InvariantCulture, out var deltaMs) && _viewModel != null)
        {
            await _viewModel.AdjustAudioDelayAsync(TimeSpan.FromMilliseconds(deltaMs));
        }
    }

    private async void ResetAudioDelayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
        {
            await _viewModel.ResetAudioDelayAsync();
        }
    }

    private void ChaptersButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.ToggleDrawer(PlayerDrawerKind.Chapters);
    }

    private async void AddBookmarkButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
        {
            await _viewModel.AddBookmarkAtCurrentAsync();
            await PopulateBookmarksUiAsync();
        }
    }

    private async void GenerateChaptersButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;

        if (!await ConfirmChaptersConsentAsync())
        {
            return;
        }

        GenerateChaptersButton.IsEnabled = false;
        try
        {
            await _viewModel.GenerateChaptersAsync();
        }
        finally
        {
            GenerateChaptersButton.IsEnabled = _viewModel.CanGenerateChapters;
        }
        await PopulateChaptersUiAsync();
    }

    private async Task<bool> ConfirmChaptersConsentAsync()
    {
        if (XamlRoot is not { } root)
        {
            return false;
        }

        var dialog = new ContentDialog
        {
            Title = "Generate AI chapters?",
            Content = new TextBlock
            {
                Text = "This sends this file's transcript to your configured AI provider to produce chapter titles. " +
                       "The picture is never uploaded. Do you want to continue?",
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = "Generate",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = root,
        };

        try
        {
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        catch (Exception)
        {
            // Another ContentDialog was already open (WinUI allows only one per XamlRoot); treat
            // as declined rather than crash — see the ShowInfoDialogAsync note above.
            return false;
        }
    }

    private void DrawerCloseButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.CloseDrawer();
    }

    private async void SpeedPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && double.TryParse(tag, System.Globalization.CultureInfo.InvariantCulture, out var speed) && _viewModel != null)
        {
            await _viewModel.SetRateAsync(speed);
        }
    }

    private async void SpeedSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_viewModel != null && Math.Abs(_viewModel.Rate - e.NewValue) > 0.01)
        {
            await _viewModel.SetRateAsync(e.NewValue);
        }
    }

    // ── Controls Auto-Hide (PS-204) ─────────────────────────────────────────

    private void AutoHideTimer_Tick(object? sender, object e)
    {
        if (_viewModel?.IsPlaying == true && !_isPointerOverControls && !_isDraggingSlider && _viewModel?.IsDrawerOpen != true)
        {
            HideControls();
        }
    }

    private void ShowControls()
    {
        TopNavBar.Opacity = 1.0;
        TopNavBar.IsHitTestVisible = true;
        TransportBar.Opacity = 1.0;
        TransportBar.IsHitTestVisible = true;

        _autoHideTimer?.Stop();
        _autoHideTimer?.Start();
    }

    private void HideControls()
    {
        TopNavBar.Opacity = 0.0;
        TopNavBar.IsHitTestVisible = false;
        TransportBar.Opacity = 0.0;
        TransportBar.IsHitTestVisible = false;
    }

    private void RootPlayerGrid_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        ShowControls();
    }

    private void Controls_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _isPointerOverControls = true;
        ShowControls();
    }

    private void Controls_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _isPointerOverControls = false;
    }

    private async void VideoSurface_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        ShowControls();
        if (_viewModel != null)
        {
            await _viewModel.PlayPauseAsync();
        }
    }

    private void VideoSurface_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        ToggleFullScreen();
        e.Handled = true;
    }

    // ── Interactive Timeline & Hover Preview (PS-203) ───────────────────────

    private void TimelineSlider_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        ScrubTooltip.Visibility = Visibility.Visible;
        ShowScrubPreview(e);
    }

    private void TimelineSlider_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_viewModel == null || TimelineSlider.ActualWidth <= 0) return;

        ShowScrubPreview(e);
    }

    /// <summary>
    /// Drives the scrub hover popup: an ffmpeg-generated filmstrip cell (PS-704) when one is
    /// available, otherwise the plain timestamp tooltip. Never touches playback — preview is purely
    /// visual and only reads the cursor fraction.
    /// </summary>
    private void ShowScrubPreview(PointerRoutedEventArgs e)
    {
        if (_viewModel == null || TimelineSlider.ActualWidth <= 0) return;

        var pt = e.GetCurrentPoint(TimelineSlider);
        var fraction = Math.Clamp(pt.Position.X / TimelineSlider.ActualWidth, 0.0, 1.0);
        var percent = fraction * 100.0;

        // Prefer the thumbnail filmstrip when it exists — it already shows position, so the label
        // tooltip is hidden to avoid stacking two popups on top of each other.
        if (_viewModel.ThumbnailStrip is { Success: true } strip)
        {
            ScrubTooltip.Visibility = Visibility.Collapsed;
            UpdateThumbnailPreview(strip, _viewModel.GetThumbnailCellIndex(percent), pt.Position.X, fraction);
            return;
        }

        ThumbnailPreview.Visibility = Visibility.Collapsed;
        ScrubTooltipText.Text = _viewModel.GetHoverPreviewText(percent);
        ScrubTooltip.Visibility = Visibility.Visible;

        // Position tooltip centered above the cursor
        var tooltipX = Math.Clamp(pt.Position.X - 40, 0, Math.Max(0, TimelineSlider.ActualWidth - 80));
        ScrubTooltip.Margin = new Thickness(tooltipX, -34, 0, 0);
    }

    /// <summary>Positions the little filmstrip-preview flower above the scrubbed column/row cell.</summary>
    private void UpdateThumbnailPreview(ThumbnailStripResult strip, int cellIndex, double pointerX, double fraction)
    {
        if (strip.CellWidth <= 0 || strip.CellHeight <= 0 || strip.Columns <= 0 || strip.Rows <= 0)
        {
            ThumbnailPreview.Visibility = Visibility.Collapsed;
            return;
        }

        var cellWidth = strip.CellWidth;
        var cellHeight = strip.CellHeight;
        var col = cellIndex % strip.Columns;
        var row = cellIndex / strip.Columns;

        if (!string.Equals(_lastThumbnailStripPath, strip.StripPath, StringComparison.OrdinalIgnoreCase))
        {
            ThumbnailPreviewImage.Source = new BitmapImage(new Uri(strip.StripPath!));
            _lastThumbnailStripPath = strip.StripPath;
        }

        ThumbnailPreviewClip.Width = cellWidth;
        ThumbnailPreviewClip.Height = cellHeight;
        ThumbnailPreviewClip.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, cellWidth, cellHeight) };

        ThumbnailPreviewImage.Width = strip.Columns * cellWidth;
        ThumbnailPreviewImage.Height = strip.Rows * cellHeight;
        ThumbnailPreviewImage.Margin = new Thickness(-col * cellWidth, -row * cellHeight, 0, 0);

        // Center the popup over the cursor, clamped into the slider's horizontal extent.
        var previewX = Math.Clamp(pointerX - cellWidth / 2.0, 0, Math.Max(0, TimelineSlider.ActualWidth - cellWidth));
        ThumbnailPreview.Margin = new Thickness(previewX, -(cellHeight + 16), 0, 0);
        ThumbnailPreview.Visibility = Visibility.Visible;
    }

    private void TimelineSlider_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDraggingSlider)
        {
            ScrubTooltip.Visibility = Visibility.Collapsed;
            ThumbnailPreview.Visibility = Visibility.Collapsed;
        }
    }

    private void TimelineSlider_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _isDraggingSlider = false;
        _viewModel?.EndSeek();
        ScrubTooltip.Visibility = Visibility.Collapsed;
        ThumbnailPreview.Visibility = Visibility.Collapsed;
    }

    private async void TimelineSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isUpdatingSliderFromVm || _viewModel == null) return;
        _isDraggingSlider = true;
        _viewModel.BeginSeek();
        await _viewModel.SeekToPercentAsync(e.NewValue);
    }

    // ── Transport Actions ───────────────────────────────────────────────────

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (Frame != null && Frame.CanGoBack)
        {
            Frame.GoBack();
        }
    }

    private async void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
        {
            await _viewModel.PlayPauseAsync();
        }
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
        {
            await _viewModel.StopAsync();
        }
    }

    private async void SkipBackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
        {
            await _viewModel.SkipBackAsync();
        }
    }

    private async void SkipForwardButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
        {
            await _viewModel.SkipForwardAsync();
        }
    }

    private async void PlayPreviousButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
        {
            await _viewModel.PlayPreviousAsync();
        }
    }

    private async void PlayNextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
        {
            await _viewModel.PlayNextAsync();
        }
    }

    private void ShuffleButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.ToggleShuffleAsync();
    }

    private void RepeatButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.CycleRepeatAsync();
    }

    private void RateButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel?.ToggleDrawer(PlayerDrawerKind.PlaybackSpeed);
    }

    private async void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isUpdatingSliderFromVm || _viewModel == null) return;
        await _viewModel.SetVolumeAsync((int)e.NewValue);
    }

    private async void MuteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
        {
            await _viewModel.ToggleMuteAsync();
        }
    }

    private void FullScreenButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleFullScreen();
    }

    private void CinemaButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleCinemaMode();
    }

    private void MiniPlayerButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleCompactOverlay();
    }

    // ── Keyboard Navigation (PS-203, PS-205, PS-206, PS-207) ────────────────

    private async void RootPlayerGrid_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        ShowControls();
        if (_viewModel == null) return;

        var isShiftDown = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        switch (e.Key)
        {
            case VirtualKey.Space:
                await _viewModel.PlayPauseAsync();
                e.Handled = true;
                break;

            case VirtualKey.Left:
                await _viewModel.SeekRelativeAsync(-(isShiftDown ? LongSeek : ShortSeek));
                e.Handled = true;
                break;

            case VirtualKey.Right:
                await _viewModel.SeekRelativeAsync(isShiftDown ? LongSeek : ShortSeek);
                e.Handled = true;
                break;

            case VirtualKey.Up:
                await _viewModel.SetVolumeAsync(Math.Min(100, _viewModel.Volume + 5));
                e.Handled = true;
                break;

            case VirtualKey.Down:
                await _viewModel.SetVolumeAsync(Math.Max(0, _viewModel.Volume - 5));
                e.Handled = true;
                break;

            case VirtualKey.M:
                await _viewModel.ToggleMuteAsync();
                e.Handled = true;
                break;

            case VirtualKey.R:
                await _viewModel.CycleRateAsync();
                e.Handled = true;
                break;

            case VirtualKey.S:
                await _viewModel.StopAsync();
                e.Handled = true;
                break;

            case VirtualKey.A:
                _viewModel.ToggleDrawer(PlayerDrawerKind.AudioTracks);
                e.Handled = true;
                break;

            case VirtualKey.V:
                _viewModel.ToggleDrawer(PlayerDrawerKind.SubtitleTracks);
                e.Handled = true;
                break;

            case VirtualKey.G:
                await _viewModel.AdjustSubtitleDelayAsync(TimeSpan.FromMilliseconds(-50));
                e.Handled = true;
                break;

            case VirtualKey.H:
                await _viewModel.AdjustSubtitleDelayAsync(TimeSpan.FromMilliseconds(50));
                e.Handled = true;
                break;

            case VirtualKey.J:
                await _viewModel.AdjustAudioDelayAsync(TimeSpan.FromMilliseconds(-50));
                e.Handled = true;
                break;

            case VirtualKey.K:
                await _viewModel.AdjustAudioDelayAsync(TimeSpan.FromMilliseconds(50));
                e.Handled = true;
                break;

            case VirtualKey.I:
                _viewModel.ToggleDrawer(PlayerDrawerKind.MediaInfo);
                e.Handled = true;
                break;

            case VirtualKey.E:
                _viewModel.ToggleDrawer(PlayerDrawerKind.Equalizer);
                e.Handled = true;
                break;

            case VirtualKey.P:
                ToggleCompactOverlay();
                e.Handled = true;
                break;

            case (VirtualKey)190: // Period (.)
                await _viewModel.PlayNextAsync();
                e.Handled = true;
                break;

            case (VirtualKey)188: // Comma (,)
                await _viewModel.PlayPreviousAsync();
                e.Handled = true;
                break;

            case (VirtualKey)219: // Left bracket ([) — Mark In
                _viewModel.SetMarkIn();
                e.Handled = true;
                break;

            case (VirtualKey)221: // Right bracket (]) — Mark Out
                _viewModel.SetMarkOut();
                e.Handled = true;
                break;

            case VirtualKey.F:
                ToggleFullScreen();
                e.Handled = true;
                break;

            case VirtualKey.F11:
                ToggleFullScreen();
                e.Handled = true;
                break;

            case VirtualKey.C:
                ToggleCinemaMode();
                e.Handled = true;
                break;

            case VirtualKey.Enter:
                var isAltDown = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu)
                    .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
                if (isAltDown)
                {
                    ToggleFullScreen();
                    e.Handled = true;
                }
                break;

            case VirtualKey.Escape:
                if (_viewModel.IsDrawerOpen)
                {
                    _viewModel.CloseDrawer();
                    e.Handled = true;
                }
                else if (_windowService?.IsFullScreen == true)
                {
                    PauseVisualizerAcrossWindowChange();
                    _windowService.SetFullScreen(false);
                    e.Handled = true;
                }
                else if (Frame != null && Frame.CanGoBack)
                {
                    Frame.GoBack();
                    e.Handled = true;
                }
                break;
        }
    }
}
