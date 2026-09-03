using PlaySphere.Domain.Visualizer;

namespace PlaySphere.Application.Visualizer;

/// <summary>
/// A single-producer / single-consumer latest-frame channel between the audio analysis thread
/// and the renderer thread (PICKLE.md §33). Only the most recent <see cref="AudioAnalysisFrame"/>
/// is retained, so a slow renderer never backs up the analyser — frames are dropped before audio
/// is. The playback thread never blocks on this channel's capacity.
/// </summary>
public interface ILatestAnalysisFrameProvider
{
    /// <summary>Publishes a frame, replacing any prior frame (old frames are dropped).</summary>
    void Publish(AudioAnalysisFrame frame);

    /// <summary>Reads the latest frame, returning null when none has been published yet.</summary>
    AudioAnalysisFrame? TryReadLatest();
}
