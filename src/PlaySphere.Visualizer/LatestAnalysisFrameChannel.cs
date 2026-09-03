using PlaySphere.Application.Visualizer;
using PlaySphere.Domain.Visualizer;

namespace PlaySphere.Visualizer;

/// <summary>
/// Thread-safe latest-frame channel (see <see cref="ILatestAnalysisFrameProvider"/>). Retains only
/// the most recent <see cref="AudioAnalysisFrame"/>; publishing replaces the previous one so a slow
/// renderer never backs up the analyser. Reading is non-blocking.
/// </summary>
public sealed class LatestAnalysisFrameChannel : ILatestAnalysisFrameProvider
{
    private AudioAnalysisFrame? _latest;

    /// <inheritdoc />
    public void Publish(AudioAnalysisFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        Volatile.Write(ref _latest, frame);
    }

    /// <inheritdoc />
    public AudioAnalysisFrame? TryReadLatest() => Volatile.Read(ref _latest);
}
