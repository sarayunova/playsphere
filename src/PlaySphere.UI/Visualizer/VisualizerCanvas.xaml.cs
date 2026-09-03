using System;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PlaySphere.Application.Visualizer;
using PlaySphere.Domain.Visualizer;

namespace PlaySphere.UI.Visualizer;

/// <summary>
/// Win2D visualizer surface (VIS-04). Hosts a <see cref="CanvasAnimatedControl"/> that reads the
/// latest <see cref="AudioAnalysisFrame"/> from an <see cref="ILatestAnalysisFrameProvider"/> and
/// draws it with the active <see cref="IRenderPreset"/>.
///
/// The render loop runs on Win2D's animation thread — it never blocks the playback/audio thread
/// (the provider retains only the newest frame, so a slow renderer drops visualizer frames before
/// audio is ever dropped, per PICKLE.md §33/§35). The UI still never touches libVLC/FFmpeg: the
/// data seam is the pure <see cref="ILatestAnalysisFrameProvider"/> contract.
/// </summary>
public sealed partial class VisualizerCanvas : UserControl
{
    private CanvasAnimatedControl? _canvas;
    private ILatestAnalysisFrameProvider? _frameProvider;
    private IRenderPreset? _preset;
    private bool _reducedMotion;

    /// <summary>Background cleared on every frame.</summary>
    public Windows.UI.Color BackgroundColor { get; set; } = Windows.UI.Color.FromArgb(0xFF, 0x0E, 0x11, 0x17);

    public VisualizerCanvas()
    {
        InitializeComponent();
        // The Win2D canvas is created lazily by the hosting page via <see cref="EnsureReady"/> only
        // when the visualizer is actually shown. All creation/draw faults are contained so a Win2D
        // device/first-frame failure never crashes the player.
        Unloaded += (_, _) => DestroyCanvas();
    }

    /// <summary>
    /// Ensures the underlying <see cref="CanvasAnimatedControl"/> exists. Called by the hosting page
    /// when it actually shows the visualizer. Exceptions are contained so a Win2D device/first-frame
    /// failure never crashes the player.
    /// </summary>
    public void EnsureReady() => SafeCreateCanvas();

    private void SafeCreateCanvas()
    {
        if (!CanAnimate())
        {
            return;
        }

        try
        {
            CreateCanvas();
        }
        catch (Exception ex)
        {
            // A Win2D device/first-frame failure must never crash the player or the page.
            System.Diagnostics.Debug.WriteLine($"VisualizerCanvas: failed to create the Win2D canvas: {ex}");
        }
    }

    /// <summary>The analysis-frame source this surface renders.</summary>
    public ILatestAnalysisFrameProvider? FrameProvider
    {
        get => _frameProvider;
        set
        {
            _frameProvider = value;
            if (_canvas != null)
            {
                _canvas.Paused = !CanAnimate();
            }
        }
    }

    /// <summary>The active visualizer drawing routine.</summary>
    public IRenderPreset? Preset
    {
        get => _preset;
        set
        {
            _preset = value;
            if (_canvas != null)
            {
                _canvas.Invalidate();
            }
        }
    }

    /// <summary>Pauses/resumes the render loop. Set to true while playback is stopped/paused.</summary>
    public bool IsPaused
    {
        set
        {
            if (_canvas != null)
            {
                _canvas.Paused = value || !CanAnimate();
            }
        }
    }

    /// <summary>
    /// Caps the render loop at 30 FPS when reduced motion / power-saver is preferred (PICKLE.md
    /// §35/§36). Windows' reduced-motion preference is respected by halving the frame rate instead
    /// of driving the display harder.
    /// </summary>
    public bool ReducedMotion
    {
        get => _reducedMotion;
        set
        {
            _reducedMotion = value;
            if (_canvas != null)
            {
                _canvas.TargetElapsedTime = value
                    ? new System.TimeSpan(0, 0, 0, 0, 33)   // ~30 FPS
                    : new System.TimeSpan(0, 0, 0, 0, 16);  // ~60 FPS
            }
        }
    }

    private void CreateCanvas()
    {
        if (_canvas != null)
        {
            return;
        }

        var canvas = new CanvasAnimatedControl
        {
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(BackgroundColor),
            IsTabStop = false,
        };
        canvas.Draw += Canvas_Draw;
        HostRoot.Children.Add(canvas);
        canvas.Paused = !CanAnimate();
        canvas.TargetElapsedTime = _reducedMotion
            ? new System.TimeSpan(0, 0, 0, 0, 33)
            : new System.TimeSpan(0, 0, 0, 0, 16);
        _canvas = canvas;
    }

    private void DestroyCanvas()
    {
        var canvas = _canvas;
        _canvas = null;
        if (canvas != null)
        {
            canvas.Draw -= Canvas_Draw;
            canvas.RemoveFromVisualTree();
        }
    }

    private bool CanAnimate()
        => _frameProvider != null && _preset != null;

    private void Canvas_Draw(ICanvasAnimatedControl sender, CanvasAnimatedDrawEventArgs args)
    {
        try
        {
            var frame = _frameProvider?.TryReadLatest();
            args.DrawingSession.Clear(BackgroundColor);

            var preset = _preset;
            if (preset == null || frame == null)
            {
                return;
            }

            preset.Draw(args.DrawingSession, frame, (float)sender.Size.Width, (float)sender.Size.Height);
        }
        catch (Exception ex)
        {
            // A rendering fault must not take down the audio/playback thread or the app.
            System.Diagnostics.Debug.WriteLine($"VisualizerCanvas: draw fault: {ex}");
        }
    }
}
