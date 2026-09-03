namespace PlaySphere.UI.Services;

/// <summary>
/// Service abstraction for controlling application window modes (Fullscreen, Cinema Mode, CompactOverlay, etc.)
/// Decoupled from Windows App SDK AppWindow so it can be mocked in unit tests.
/// </summary>
public interface IWindowService
{
    /// <summary>True when the window is currently in immersive fullscreen mode.</summary>
    bool IsFullScreen { get; }

    /// <summary>True when Cinema mode (expanded canvas, dimmed background) is active.</summary>
    bool IsCinemaMode { get; }

    /// <summary>True when Picture-in-Picture / Mini-player (CompactOverlay) is active.</summary>
    bool IsCompactOverlay { get; }

    /// <summary>Toggles fullscreen on or off.</summary>
    void ToggleFullScreen();

    /// <summary>Sets explicit fullscreen state.</summary>
    void SetFullScreen(bool fullScreen);

    /// <summary>Toggles cinema mode on or off.</summary>
    void ToggleCinemaMode();

    /// <summary>Sets explicit cinema mode state.</summary>
    void SetCinemaMode(bool cinema);

    /// <summary>Toggles compact overlay / mini-player mode on or off.</summary>
    void ToggleCompactOverlay();

    /// <summary>Sets explicit compact overlay / mini-player state.</summary>
    void SetCompactOverlay(bool compactOverlay);

    /// <summary>Raised when fullscreen state transitions.</summary>
    event EventHandler? FullScreenChanged;

    /// <summary>Raised when cinema mode transitions.</summary>
    event EventHandler? CinemaModeChanged;

    /// <summary>Raised when compact overlay mode transitions.</summary>
    event EventHandler? CompactOverlayChanged;
}
