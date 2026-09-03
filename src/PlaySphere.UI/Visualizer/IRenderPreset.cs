using Microsoft.Graphics.Canvas;
using PlaySphere.Domain.Visualizer;

namespace PlaySphere.UI.Visualizer;

/// <summary>
/// Drawing contract for a single visualizer preset. Each preset owns its own rendering
/// logic for one frame of audio analysis data. Presets live in the UI layer (Win2D types
/// are required) and are consumed by <see cref="VisualizerCanvas"/>.
/// </summary>
public interface IRenderPreset
{
    /// <summary>Display name shown in the preset picker.</summary>
    string Name { get; }

    /// <summary>
    /// Draws one frame. Called on the UI thread inside a <see cref="CanvasControl.Draw"/> handler.
    /// Implementations may assume the session is already cleared.
    /// </summary>
    void Draw(CanvasDrawingSession session, AudioAnalysisFrame frame, float width, float height);
}
