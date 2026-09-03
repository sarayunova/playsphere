using PlaySphere.Application.Visualizer;
using PlaySphere.Domain.Media;

namespace PlaySphere.Playback.Abstractions;

/// <summary>
/// Abstraction over a media playback engine (per PICKLE.md §15).
/// The UI must never call libVLC directly — every playback capability flows through this
/// contract. Implementations take full responsibility for native lifecycle (init/dispose),
/// threading and state marshalling.
/// </summary>
public interface IMediaPlaybackEngine : IDisposable
{
    /// <summary>Current engine state (PICKLE.md §16 state machine).</summary>
    PlaybackState State { get; }

    /// <summary>Path of the currently loaded media, or null when nothing is loaded.</summary>
    string? LoadedFilePath { get; }

    /// <summary>Current playback position.</summary>
    TimeSpan Position { get; }

    /// <summary>Duration of the loaded media, or <see cref="TimeSpan.Zero"/> when not loaded.</summary>
    TimeSpan Duration { get; }

    /// <summary>
    /// True when the loaded media supports seeking (a finite file). False for live/streaming
    /// sources whose duration is interminable, for which scrubbing is meaningless.
    /// </summary>
    bool IsSeekable { get; }

    /// <summary>Current playback rate, clamped to <see cref="PlaybackConstraints"/>.</summary>
    double Rate { get; }

    /// <summary>Current volume (0–100).</summary>
    int Volume { get; }

    /// <summary>True when the engine is muted.</summary>
    bool IsMuted { get; }

    /// <summary>Current subtitle delay offset (positive = delayed, negative = advanced).</summary>
    TimeSpan SubtitleDelay { get; }

    /// <summary>Current audio delay offset (positive = delayed, negative = advanced).</summary>
    TimeSpan AudioDelay { get; }

    /// <summary>True when equalizer filter is enabled and actively filtering audio.</summary>
    bool IsEqualizerEnabled { get; }

    /// <summary>Current equalizer configuration snapshot (preamp, band gains, active preset).</summary>
    EqualizerConfiguration EqualizerConfig { get; }

    /// <summary>Identifier of the currently selected audio output device, or null when unset.</summary>
    string? ActiveAudioOutputDeviceId { get; }

    /// <summary>Current stereo/mono downmix channel mode.</summary>
    AudioChannelMode AudioChannel { get; }

    /// <summary>Current loudness normalization / night mode toggle state.</summary>
    AudioEnhancementOptions AudioEnhancements { get; }

    /// <summary>Current subtitle rendering style.</summary>
    SubtitleStyleOptions SubtitleStyle { get; }

    /// <summary>Metadata (tracks, duration) of the loaded media.</summary>
    MediaMetadata Metadata { get; }

    /// <summary>Loads media into the engine and transitions to Ready. Errors are recoverable:
    /// a failed load must not prevent a subsequent successful load.</summary>
    Task LoadAsync(MediaFile media, CancellationToken cancellationToken = default);

    /// <summary>Starts or resumes playback.</summary>
    Task PlayAsync(CancellationToken cancellationToken = default);

    /// <summary>Pauses playback, retaining position.</summary>
    Task PauseAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops playback and returns to a stopped state.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>Seeks to <paramref name="target"/>. Positions outside the media clamp to the nearest boundary.</summary>
    Task SeekAsync(TimeSpan target, CancellationToken cancellationToken = default);

    /// <summary>Sets playback rate; clamped to <see cref="PlaybackConstraints"/>.</summary>
    Task SetRateAsync(double rate, CancellationToken cancellationToken = default);

    /// <summary>Sets volume; clamped to <see cref="PlaybackConstraints"/>.</summary>
    Task SetVolumeAsync(int volume, CancellationToken cancellationToken = default);

    /// <summary>Mutes or unmutes playback.</summary>
    Task SetMutedAsync(bool muted, CancellationToken cancellationToken = default);

    /// <summary>Selects the active audio track (null disables/none). Id matches AudioTracks entries.</summary>
    Task SelectAudioTrackAsync(int? trackId, CancellationToken cancellationToken = default);

    /// <summary>Selects the active subtitle track (null hides subtitles). Id matches SubtitleTracks entries.</summary>
    Task SelectSubtitleTrackAsync(int? trackId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads an external subtitle file (SRT, ASS, SSA, VTT, SUB, etc.) and makes it
    /// the active subtitle track. The returned track ID can be used with
    /// <see cref="SelectSubtitleTrackAsync"/> to toggle visibility later.
    /// Throws <see cref="FileNotFoundException"/> if the path does not exist.
    /// </summary>
    Task<int> LoadExternalSubtitleAsync(string subtitlePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the subtitle delay (offset). Clamped between <see cref="PlaybackConstraints.MinSubtitleDelay"/>
    /// and <see cref="PlaybackConstraints.MaxSubtitleDelay"/>.
    /// </summary>
    Task SetSubtitleDelayAsync(TimeSpan delay, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the audio delay (A/V sync offset). Clamped between <see cref="PlaybackConstraints.MinAudioDelay"/>
    /// and <see cref="PlaybackConstraints.MaxAudioDelay"/>.
    /// </summary>
    Task SetAudioDelayAsync(TimeSpan delay, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enables or disables the 10-band audio equalizer filter.
    /// </summary>
    Task SetEqualizerEnabledAsync(bool enabled, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a standard equalizer preset by name (e.g. "Flat", "Rock", "Pop", "Full Bass").
    /// </summary>
    Task SetEqualizerPresetAsync(string presetName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the gain for a specific equalizer frequency band (0 to 9). Clamped between -20.0 dB and +20.0 dB.
    /// </summary>
    Task SetEqualizerBandGainAsync(uint bandIndex, float gainDb, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the preamp gain in decibels. Clamped between -20.0 dB and +20.0 dB.
    /// </summary>
    Task SetEqualizerPreampAsync(float preampDb, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets the equalizer to 0 dB flat gain across all 10 bands and 0 dB preamp.
    /// </summary>
    Task ResetEqualizerAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enumerates the system audio output devices currently available. Not all outputs support
    /// enumeration — an empty list does not necessarily mean playback is impossible.
    /// </summary>
    IReadOnlyList<AudioOutputDeviceInfo> GetAudioOutputDevices();

    /// <summary>
    /// Selects an explicit audio output device by id (from <see cref="GetAudioOutputDevices"/>).
    /// The device change takes effect after playback is stopped and restarted; it does not move
    /// audio for an already-playing stream immediately.
    /// </summary>
    Task SetAudioOutputDeviceAsync(string deviceId, CancellationToken cancellationToken = default);

    /// <summary>Sets the stereo/mono downmix channel mode.</summary>
    Task SetAudioChannelAsync(AudioChannelMode mode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enables/disables loudness normalization and night mode (dynamic range compression).
    /// Applied via the engine's per-media audio filter chain — takes effect starting with the
    /// next <see cref="LoadAsync"/>, not on the currently loaded media.
    /// </summary>
    Task SetAudioEnhancementsAsync(AudioEnhancementOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets subtitle rendering style (currently: text scale). Applied via the engine's per-media
    /// options — takes effect starting with the next <see cref="LoadAsync"/>, not on the
    /// currently loaded media.
    /// </summary>
    Task SetSubtitleStyleAsync(SubtitleStyleOptions style, CancellationToken cancellationToken = default);

    /// <summary>True once a render target has been attached via <see cref="AttachVideoSurfaceAsync"/>.</summary>
    bool IsVideoSurfaceAttached { get; }

    /// <summary>
    /// Attaches the engine's video renderer to the given <see cref="IVideoSurface"/>. The engine
    /// routes decoded video frames into the surface and calls
    /// <see cref="IVideoSurface.SetRenderer"/> with its renderer. Safe to call once, again, or
    /// never (audio-only media never attaches a surface). A redundant attach of the same surface
    /// is a no-op; attaching a different surface moves rendering to it. If the surface exposes
    /// <see cref="IVideoSurface.SwapChainOptions"/>, the engine re-creates its renderer against a
    /// render target constructed with those options before display.
    /// </summary>
    Task AttachVideoSurfaceAsync(IVideoSurface surface, CancellationToken cancellationToken = default);

    /// <summary>
    /// Detaches the video surface and clears its renderer via
    /// <see cref="IVideoSurface.ClearRenderer"/>. The engine keeps playing without a render
    /// target; re-attaching later resumes display. Safe to call with no surface attached.
    /// </summary>
    Task DetachVideoSurfaceAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// True once a visualizer PCM source has been attached via
    /// <see cref="AttachVisualizerSourceAsync"/>.
    /// </summary>
    bool IsVisualizerAttached { get; }

    /// <summary>
    /// Attaches a visualizer audio source to the engine's decoded PCM stream. The engine taps
    /// the decoded audio and pushes it to <paramref name="source"/> for analysis. Per the
    /// libVLC audio-callback contract (ADR-0008), while a tap is attached the engine produces
    /// <b>no speaker output</b> — video, timeline and subtitles continue, but audio is silenced
    /// until <see cref="DetachVisualizerSourceAsync"/> restores it. Safe to call once, again
    /// (replaces the prior source), or never.
    /// </summary>
    Task AttachVisualizerSourceAsync(IVisualizerAudioSource source, CancellationToken cancellationToken = default);

    /// <summary>
    /// Detaches the visualizer audio source and restores normal engine audio output. Safe to
    /// call with no source attached.
    /// </summary>
    Task DetachVisualizerSourceAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Captures a still of the currently loaded media to <paramref name="outputPath"/> via the
    /// engine's rendered video surface (libVLC <c>video.takeSnapshot</c>). A zero width/height
    /// means the native (video) size; otherwise the still is scaled. Returns true when a frame
    /// was written. Best-effort: returns false (never throws) if nothing is rendering, the
    /// output cannot be written, or the callback fails.
    /// </summary>
    Task<bool> TakeSnapshotAsync(string outputPath, uint width = 0, uint height = 0, CancellationToken cancellationToken = default);

    /// <summary>Raised whenever the state machine transitions.</summary>
    event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;

    /// <summary>Raised periodically / after seeks with the current position.</summary>
    event EventHandler<PlaybackPositionChangedEventArgs>? PositionChanged;

    /// <summary>Raised when the media plays to its end.</summary>
    event EventHandler<PlaybackEndedEventArgs>? EndReached;

    /// <summary>Raised on any engine/playback failure. Errors are always recoverable.</summary>
    event EventHandler<PlaybackErrorEventArgs>? ErrorOccurred;

    /// <summary>Raised when loaded media exposes (or updates) its tracks/duration.</summary>
    event EventHandler<PlaybackTracksChangedEventArgs>? TracksChanged;

    /// <summary>Raised when equalizer configuration (state, preset, preamp, or band gains) changes.</summary>
    event EventHandler<PlaybackEqualizerChangedEventArgs>? EqualizerChanged;
}