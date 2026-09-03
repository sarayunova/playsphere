using Microsoft.UI.Windowing;

namespace PlaySphere.UI.Services;

/// <summary>
/// Default implementation of <see cref="IWindowService"/> integrating with
/// Windows App SDK <see cref="AppWindow"/> for desktop window mode management.
/// </summary>
public sealed class AppWindowService : IWindowService
{
    private AppWindow? _appWindow;
    private bool _isFullScreen;
    private bool _isCinemaMode;
    private bool _isCompactOverlay;

    public AppWindowService(AppWindow? appWindow = null)
    {
        _appWindow = appWindow;
        SyncFromPresenter();
    }

    /// <summary>Attaches or updates the active <see cref="AppWindow"/> instance.</summary>
    public void Attach(AppWindow appWindow)
    {
        _appWindow = appWindow ?? throw new ArgumentNullException(nameof(appWindow));
        SyncFromPresenter();
    }

    private void SyncFromPresenter()
    {
        if (_appWindow?.Presenter is FullScreenPresenter)
        {
            _isFullScreen = true;
            _isCompactOverlay = false;
        }
        else if (_appWindow?.Presenter is CompactOverlayPresenter)
        {
            _isFullScreen = false;
            _isCompactOverlay = true;
        }
    }

    public bool IsFullScreen => _isFullScreen;
    public bool IsCinemaMode => _isCinemaMode;
    public bool IsCompactOverlay => _isCompactOverlay;

    public event EventHandler? FullScreenChanged;
    public event EventHandler? CinemaModeChanged;
    public event EventHandler? CompactOverlayChanged;

    public void ToggleFullScreen() => SetFullScreen(!_isFullScreen);

    public void SetFullScreen(bool fullScreen)
    {
        if (_isFullScreen == fullScreen) return;

        _isFullScreen = fullScreen;

        if (_appWindow != null)
        {
            if (_isFullScreen)
            {
                if (_isCompactOverlay)
                {
                    _isCompactOverlay = false;
                    CompactOverlayChanged?.Invoke(this, EventArgs.Empty);
                }
                _appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
            }
            else
            {
                _appWindow.SetPresenter(AppWindowPresenterKind.Default);
            }
        }

        FullScreenChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleCinemaMode() => SetCinemaMode(!_isCinemaMode);

    public void SetCinemaMode(bool cinema)
    {
        if (_isCinemaMode == cinema) return;

        _isCinemaMode = cinema;
        CinemaModeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ToggleCompactOverlay() => SetCompactOverlay(!_isCompactOverlay);

    public void SetCompactOverlay(bool compactOverlay)
    {
        if (_isCompactOverlay == compactOverlay) return;

        _isCompactOverlay = compactOverlay;

        if (_appWindow != null)
        {
            if (_isCompactOverlay)
            {
                if (_isFullScreen)
                {
                    _isFullScreen = false;
                    FullScreenChanged?.Invoke(this, EventArgs.Empty);
                }
                _appWindow.SetPresenter(AppWindowPresenterKind.CompactOverlay);
            }
            else
            {
                _appWindow.SetPresenter(AppWindowPresenterKind.Default);
            }
        }

        CompactOverlayChanged?.Invoke(this, EventArgs.Empty);
    }
}
