namespace PlaySphere.Playback.Abstractions;

/// <summary>
/// A render target that the playback engine can draw decoded video frames into.
///
/// Implemented by platform-specific video surface hosts in the view layer (this abstraction
/// deliberately never names libVLC/WinUI types — graphic binding is carried opaquely through
/// <see cref="SetRenderer"/>). This keeps the UI/ViewModel clean of libVLC references while
/// still letting the engine drive rendering into whichever native surface the platform provides.
/// </summary>
public interface IVideoSurface
{
    /// <summary>
    /// Opaque native swap-chain options the underlying renderer must be constructed with
    /// (UI-thread native pointers such as <c>--winrt-swapchain</c>/<c>--winrt-d3dcontext</c>).
    /// <see langword="null"/> for surfaces that don't require construction-time options.
    /// </summary>
    string[]? SwapChainOptions { get; }

    /// <summary>
    /// Hands the engine's renderer (an opaque, platform-specific object — e.g. the underlying
    /// media player instance) to the surface so it can begin displaying decoded frames.
    /// The implementer casts the renderer to its known concrete type.
    /// </summary>
    void SetRenderer(object renderer);

    /// <summary>
    /// Removes the renderer from the surface. Subsequent frames are dropped (video stops
    /// displaying) until <see cref="SetRenderer"/> is called again.
    /// </summary>
    void ClearRenderer();
}
