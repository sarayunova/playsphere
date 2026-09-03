using LibVLCSharp.Shared;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PlaySphere.Playback.Abstractions;

namespace PlaySphere.UI.Controls;

/// <summary>
/// Thin PlaySphere-owned wrapper around libVLC's WinUI <see cref="LibVLCSharp.Platforms.Windows.VideoView"/>
/// (a SwapChainPanel-based video host). Implements <see cref="IVideoSurface"/> so the playback
/// engine can attach its renderer without the page or any ViewModel ever referencing libVLC types.
///
/// This is the single view-layer integration point with libVLC's native video surface: it captures
/// the construction-time swap-chain options from the <see cref="Initialized"/> event and, on
/// <see cref="SetRenderer"/>, binds the engine's <see cref="MediaPlayer"/> to the inner
/// <see cref="LibVLCSharp.Platforms.Windows.VideoView.MediaPlayer"/> dependency property.
/// </summary>
public sealed partial class VideoSurfaceHost : UserControl, IVideoSurface
{
    private string[]? _swapChainOptions;

    public VideoSurfaceHost()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Raised (on the UI thread) once the underlying swap-chain has initialized and
    /// <see cref="SwapChainOptions"/> is populated — the signal for the hosting page to attach
    /// this surface to the playback engine.
    /// </summary>
    public event EventHandler? Initialized;

    public string[]? SwapChainOptions => _swapChainOptions;

    public void SetRenderer(object renderer)
    {
        VideoView.MediaPlayer = renderer as MediaPlayer ?? throw new ArgumentException(
            $"Renderer must be a {nameof(MediaPlayer)}; got {renderer?.GetType().Name ?? "null"}.", nameof(renderer));
    }

    public void ClearRenderer()
    {
        VideoView.MediaPlayer = null;
    }

    private void VideoView_Initialized(object? sender, LibVLCSharp.Platforms.Windows.InitializedEventArgs e)
    {
        _swapChainOptions = e.SwapChainOptions;
        Initialized?.Invoke(this, EventArgs.Empty);
    }
}
