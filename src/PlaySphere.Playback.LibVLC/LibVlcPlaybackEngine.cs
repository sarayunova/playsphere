using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using PlaySphere.Application.Visualizer;
using PlaySphere.Domain.Media;
using PlaySphere.Playback.Abstractions;
using LibVLCLib = LibVLCSharp.Shared.LibVLC;

namespace PlaySphere.Playback.LibVLC;

/// <summary>
/// libVLC-backed <see cref="IMediaPlaybackEngine"/> (PS-102..).
///
/// Owns the LibVLCSharp <see cref="LibVLCSharp.Shared.MediaPlayer"/> lifecycle and maps
/// libVLC behaviour onto the PICKLE.md §16 state machine.
/// PS-104: transport controls (Play/Pause/Stop) wired; libVLC events → engine events.
/// </summary>
public sealed class LibVlcPlaybackEngine : IMediaPlaybackEngine
{
    private readonly ILogger<LibVlcPlaybackEngine> _logger;
    private readonly LibVlcOptions _options;
    private LibVLCLib _libVlc;
    private LibVLCSharp.Shared.MediaPlayer _mediaPlayer;
    private IVideoSurface? _attachedSurface;
    private string[]? _activeSwapChainOptions;
    private bool _ownsLibVlc;
    private bool _disposed;

    // Resolved path of the last successfully loaded media. Rebuilds create a fresh MediaPlayer with
    // no media, so this is re-attached on the new player or a later Play() would return false on an
    // empty player (libVLC fails via libvlc_printerr, which never reaches the Log event).
    private string? _loadedPath;

    // The MediaFile of the last successfully loaded source (path or URI). Used for the bounded
    // auto-reconnect of dropped network streams (PICKLE.md §8 "reconnect behavior").
    private MediaFile? _loadedMedia;

    // Auto-reconnect (Phase 8) — bounded, safe reload of a dropped stream. This is intentionally
    // NOT a native player rebuild (which has a recorded native-AV history when raced mid-play):
    // it reloads the same URI onto the existing player and replays, mirroring the proven-safe
    // post-end-seek reload path. Retries are bounded and back off; when exhausted the stream is
    // left in the normal Error state.
    private const int MaxReconnectAttempts = 3;
    private static readonly TimeSpan[] ReconnectBackoff = { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4) };
    private int _reconnectAttempt;
    private CancellationTokenSource? _reconnectCts;
    private bool _reconnecting;
    private readonly object _reconnectLock = new();

    // Visualizer PCM tap (ADR-0008). Storing strong references to the delegate instances is
    // required so they survive for the lifetime of the native callback registration.
    private IVisualizerAudioSource? _visualizerSource;
    private uint _visualizerSampleRate;
    private uint _visualizerChannels;
    private LibVLCSharp.Shared.MediaPlayer.LibVLCAudioSetupCb? _audioSetupCb;
    private LibVLCSharp.Shared.MediaPlayer.LibVLCAudioPlayCb? _audioPlayCb;

    public LibVlcPlaybackEngine(
        LibVLCLib libVlc,
        ILogger<LibVlcPlaybackEngine> logger,
        LibVlcOptions? options = null)
    {
        _libVlc = libVlc ?? throw new ArgumentNullException(nameof(libVlc));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? new LibVlcOptions();
        _mediaPlayer = CreateMediaPlayer(_libVlc);

        // Wire libVLC events → engine state / public events.
        // libVLC callbacks arrive on native threads; we marshal safely via captured 'this'
        // (no UI dispatcher needed here — callers subscribe on whatever thread they want).
        WireMediaPlayerEvents(_mediaPlayer);
    }

    public PlaybackState State => _state;

    /// <summary>Path of the currently loaded media, or null when nothing is loaded.</summary>
    public string? LoadedFilePath => _loadedPath;

    private PlaybackState _state = PlaybackState.Empty;
    private double _rate = 1.0;
    private int _volume = 100;
    private bool _isMuted;
    private TimeSpan _subtitleDelay = TimeSpan.Zero;
    private TimeSpan _audioDelay = TimeSpan.Zero;
    private EqualizerConfiguration _equalizerConfig = EqualizerConfiguration.Default;
    private string? _activeAudioOutputDeviceId;
    private AudioChannelMode _audioChannel = AudioChannelMode.Stereo;
    private AudioEnhancementOptions _audioEnhancements = AudioEnhancementOptions.Default;
    private SubtitleStyleOptions _subtitleStyle = SubtitleStyleOptions.Default;

    public TimeSpan Position
    {
        get
        {
            long ms = _mediaPlayer.Time;
            return ms <= 0 ? TimeSpan.Zero : TimeSpan.FromMilliseconds(ms);
        }
    }

    public TimeSpan Duration
    {
        get
        {
            if (Metadata.Duration > TimeSpan.Zero)
            {
                return Metadata.Duration;
            }

            if (_mediaPlayer.Media is null)
            {
                return TimeSpan.Zero;
            }

            long ms = _mediaPlayer.Length;
            return ms <= 0 ? TimeSpan.Zero : TimeSpan.FromMilliseconds(ms);
        }
    }

    /// <summary>
    /// True when the loaded media can be seeked. Uses libVLC's native seekability (which is
    /// false for live/streaming sources) and falls back to "finite duration" when the native
    /// flag is unavailable. A live radio stream reports duration 0 and is not seekable, so the
    /// timeline/skip controls correctly disable for it.
    /// </summary>
    public bool IsSeekable
    {
        get
        {
            if (_mediaPlayer is null || _mediaPlayer.Media is null)
            {
                return false;
            }

            try
            {
                // libVLC's IsSeekable is authoritative for HTTP/network inputs: false for
                // interminable/live streams even before playback. Guarded because a disposed
                // native player can throw.
                return _mediaPlayer.IsSeekable;
            }
            catch
            {
                return Duration > TimeSpan.Zero;
            }
        }
    }

    public double Rate => _rate;

    public int Volume => _volume;

    public bool IsMuted => _isMuted;

    public TimeSpan SubtitleDelay => _subtitleDelay;

    public TimeSpan AudioDelay => _audioDelay;

    public bool IsEqualizerEnabled => _equalizerConfig.IsEnabled;

    public EqualizerConfiguration EqualizerConfig => _equalizerConfig;

    public string? ActiveAudioOutputDeviceId => _activeAudioOutputDeviceId;

    public AudioChannelMode AudioChannel => _audioChannel;

    public AudioEnhancementOptions AudioEnhancements => _audioEnhancements;

    public SubtitleStyleOptions SubtitleStyle => _subtitleStyle;

    public bool IsVideoSurfaceAttached => _attachedSurface is not null;

    public bool IsVisualizerAttached => _visualizerSource is not null;

    public MediaMetadata Metadata { get; private set; } = MediaMetadata.Empty;

    public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;
    public event EventHandler<PlaybackPositionChangedEventArgs>? PositionChanged;
    public event EventHandler<PlaybackEndedEventArgs>? EndReached;
    public event EventHandler<PlaybackErrorEventArgs>? ErrorOccurred;
    public event EventHandler<PlaybackTracksChangedEventArgs>? TracksChanged;
    public event EventHandler<PlaybackEqualizerChangedEventArgs>? EqualizerChanged;

    public async Task LoadAsync(MediaFile media, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(media);
        cancellationToken.ThrowIfCancellationRequested();

        // A user-initiated load replaces whatever is playing, so cancel any in-flight stream
        // auto-reconnect. The reconnect loop sets _reconnecting while it reloads so it does not
        // cancel its own in-flight task.
        if (!_reconnecting)
        {
            CancelAutoReconnect();
        }

        // Remote network streams have no filesystem path, so the local-file presence/header pre-checks
        // do not apply. libVLC builds a network Media from the URI and fails gracefully via the parse
        // status if the stream is unreachable.
        if (media.IsRemoteUri)
        {
            TransitionToLoading();
            try
            {
                await LoadNetworkAsync(media, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                FailLoad(ex);
            }
            return;
        }

        var path = ResolvePath(media);
        if (path is null || !File.Exists(path))
        {
            FailLoad(new FileNotFoundException($"Media file not found: {media.FilePath}"));
            return;
        }

        if (!HasRecognisedMediaHeader(path))
        {
            FailLoad(new InvalidDataException($"File does not appear to be a recognised media container: {Path.GetFileName(path)}"));
            return;
        }

        TransitionToLoading();

        try
        {
            await LoadCoreAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            FailLoad(ex);
        }
    }

    private async Task LoadNetworkAsync(MediaFile media, CancellationToken cancellationToken)
    {
        var uri = media.SourceUri!;
        using var vlcMedia = new LibVLCSharp.Shared.Media(_libVlc, uri);
        ApplyMetadataNoNetworkAccess(vlcMedia);
        ApplyAudioEnhancementsToMedia(vlcMedia);
        ApplySubtitleStyleToMedia(vlcMedia);
        _mediaPlayer.Stop();
        _mediaPlayer.Media = vlcMedia;

        // Eagerly acquire tracks. Network media uses ParseNetwork; a HLS/DASH manifest or livestream
        // may take a moment to probe, so allow a longer timeout.
        const int ParseTimeoutMs = 8000;
        var parsed = await Task.Run(
            () => vlcMedia.Parse(LibVLCSharp.Shared.MediaParseOptions.ParseNetwork, timeout: ParseTimeoutMs, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        if (parsed != LibVLCSharp.Shared.MediaParsedStatus.Done)
        {
            throw new IOException($"libVLC could not open stream '{uri}' (status: {parsed}).");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var metadata = BuildMetadata(vlcMedia);
        Metadata = metadata;
        _logger.LogInformation("Loaded stream '{Uri}': {Duration}ms, {Video} video, {Audio} audio, {Sub} subtitle tracks.",
            uri, metadata.Duration.TotalMilliseconds, metadata.VideoTracks.Count,
            metadata.AudioTracks.Count, metadata.SubtitleTracks.Count);

        TracksChanged?.Invoke(this, new PlaybackTracksChangedEventArgs(metadata));
        SetState(PlaybackState.Ready);
        _loadedPath = uri.AbsoluteUri;
        _loadedMedia = media;
        _reconnectAttempt = 0;
    }

    private async Task LoadCoreAsync(string path, CancellationToken cancellationToken)
    {
        using var vlcMedia = new LibVLCSharp.Shared.Media(_libVlc, path);
        ApplyMetadataNoNetworkAccess(vlcMedia);
        ApplyAudioEnhancementsToMedia(vlcMedia);
        ApplySubtitleStyleToMedia(vlcMedia);
        _mediaPlayer.Stop();
        _mediaPlayer.Media = vlcMedia;

        // Eagerly acquire tracks (avoids HEAD parsing on the UI thread later).
        // Timeout of 5000 ms guards against libVLC hanging or crashing on corrupted input.
        const int ParseTimeoutMs = 5000;
        var parsed = await Task.Run(
            () => vlcMedia.Parse(LibVLCSharp.Shared.MediaParseOptions.ParseLocal, timeout: ParseTimeoutMs, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        if (parsed != LibVLCSharp.Shared.MediaParsedStatus.Done)
        {
            throw new IOException($"libVLC could not parse media '{path}' (status: {parsed}).");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var metadata = BuildMetadata(vlcMedia);
        Metadata = metadata;
        _logger.LogInformation("Loaded media '{Path}': {Duration}ms, {Video} video, {Audio} audio, {Sub} subtitle tracks.",
            path, metadata.Duration.TotalMilliseconds, metadata.VideoTracks.Count,
            metadata.AudioTracks.Count, metadata.SubtitleTracks.Count);

        TracksChanged?.Invoke(this, new PlaybackTracksChangedEventArgs(metadata));
        SetState(PlaybackState.Ready);
        _loadedPath = path;
        _loadedMedia = MediaFile.FromPath(path);
        _reconnectAttempt = 0;
    }

    public Task PlayAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (_state is not (PlaybackState.Ready or PlaybackState.Paused or PlaybackState.Stopped))
        {
            _logger.LogWarning("PlayAsync called in invalid state {State}; ignoring.", _state);
            return Task.CompletedTask;
        }

        // libVLC raises Playing event asynchronously; state transitions happen there.
        if (!_mediaPlayer.Play())
        {
            var detail = BuildFailureContext();

            // A failed Play() on a remote stream is commonly a transient network/input issue
            // rather than a genuine fatal error (libVLC often opens the metadata probe fine but
            // cannot stand up the demux/input at play time). Reuse the Phase 8 bounded auto-reconnect
            // (reload the same URI onto the existing player and replay) so a flaky stream recovers
            // automatically instead of leaving the user at a dead-end Error chip.
            if (detail is not { Length: > 0 } && TryBeginAutoReconnect())
            {
                return Task.CompletedTask;
            }

            var err = new InvalidOperationException(
                "libVLC MediaPlayer.Play() returned false." +
                (string.IsNullOrEmpty(detail) ? "" : " " + detail));
            _logger.LogError(err, "Play failed.");
            SetState(PlaybackState.Error);
            ErrorOccurred?.Invoke(this, new PlaybackErrorEventArgs(err.Message, err));
        }

        return Task.CompletedTask;
    }

    public Task PauseAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (_state is not PlaybackState.Playing)
        {
            _logger.LogWarning("PauseAsync called in invalid state {State}; ignoring.", _state);
            return Task.CompletedTask;
        }

        // SetPause(true) suspends without resetting position; libVLC raises Paused event.
        _mediaPlayer.SetPause(true);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (_state is PlaybackState.Empty or PlaybackState.Stopped)
        {
            return Task.CompletedTask;
        }

        // libVLC raises Stopped event asynchronously; state transitions happen there.
        _mediaPlayer.Stop();
        return Task.CompletedTask;
    }

    public Task SeekAsync(TimeSpan target, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (_state is not (PlaybackState.Playing or PlaybackState.Paused or PlaybackState.Ready or PlaybackState.Stopped))
        {
            _logger.LogWarning("SeekAsync called in invalid state {State}; ignoring.", _state);
            return Task.CompletedTask;
        }

        // Clamp to [0, Duration]. Duration may be zero for some streams; skip clamping then.
        var duration = Duration;
        var clamped = duration > TimeSpan.Zero
            ? TimeSpan.FromMilliseconds(Math.Clamp(target.TotalMilliseconds, 0, duration.TotalMilliseconds))
            : TimeSpan.FromMilliseconds(Math.Max(0, target.TotalMilliseconds));

        // libVLC Time is in milliseconds; it is safe to set while playing or paused.
        // If the media is not yet playing, set position (0–1 float) instead.
        if (_state is PlaybackState.Playing or PlaybackState.Paused)
        {
            _mediaPlayer.Time = (long)clamped.TotalMilliseconds;
        }
        else if (duration > TimeSpan.Zero)
        {
            // Ready/Stopped — use fractional position so libVLC can honour it once play starts.
            _mediaPlayer.Position = (float)(clamped.TotalMilliseconds / duration.TotalMilliseconds);
        }

        _logger.LogDebug("SeekAsync: requested {Target}ms → clamped {Clamped}ms (duration {Duration}ms).",
            target.TotalMilliseconds, clamped.TotalMilliseconds, duration.TotalMilliseconds);

        // Fire PositionChanged immediately so the UI doesn't wait for the next TimeChanged tick.
        PositionChanged?.Invoke(this, new PlaybackPositionChangedEventArgs(clamped));

        return Task.CompletedTask;
    }

    public Task SetRateAsync(double rate, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        _rate = PlaybackConstraints.ClampRate(rate);
        _mediaPlayer.SetRate((float)_rate);
        _logger.LogDebug("SetRateAsync: requested {Rate} → clamped {Clamped}.", rate, _rate);
        return Task.CompletedTask;
    }

    public Task SetVolumeAsync(int volume, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        _volume = PlaybackConstraints.ClampVolume(volume);
        _mediaPlayer.Volume = _volume;
        _logger.LogDebug("SetVolumeAsync: requested {Volume} → clamped {Clamped}.", volume, _volume);
        return Task.CompletedTask;
    }

    public Task SetMutedAsync(bool muted, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        _isMuted = muted;
        _mediaPlayer.Mute = _isMuted;
        _logger.LogDebug("SetMutedAsync: {Muted}.", _isMuted);
        return Task.CompletedTask;
    }

    public Task SelectAudioTrackAsync(int? trackId, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        // trackId null → disable audio (-1 in libVLC)
        _mediaPlayer.SetAudioTrack(trackId ?? -1);
        _logger.LogDebug("SelectAudioTrackAsync: {TrackId}.", trackId?.ToString() ?? "none");
        return Task.CompletedTask;
    }

    public Task SelectSubtitleTrackAsync(int? trackId, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        // trackId null → hide subtitles (-1 in libVLC)
        _mediaPlayer.SetSpu(trackId ?? -1);
        _logger.LogDebug("SelectSubtitleTrackAsync: {TrackId}.", trackId?.ToString() ?? "none");
        return Task.CompletedTask;
    }

    public Task<int> LoadExternalSubtitleAsync(string subtitlePath, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(subtitlePath);
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(subtitlePath))
        {
            throw new FileNotFoundException($"Subtitle file not found: {subtitlePath}", subtitlePath);
        }

        var ext = Path.GetExtension(subtitlePath).ToLowerInvariant();
        if (!SubtitleExtensions.Contains(ext))
        {
            throw new NotSupportedException($"Unsupported subtitle format '{ext}'. Supported: {string.Join(", ", SubtitleExtensions)}");
        }

        if (_state is not (PlaybackState.Playing or PlaybackState.Paused or PlaybackState.Ready or PlaybackState.Stopped))
        {
            throw new InvalidOperationException($"Cannot add subtitles in state {_state}. Load media first.");
        }

        // libVLC AddSlave adds the subtitle and activates it. Returns true on success.
        bool added = _mediaPlayer.AddSlave(
            LibVLCSharp.Shared.MediaSlaveType.Subtitle,
            new Uri(subtitlePath).AbsoluteUri,
            select: true);

        if (!added)
        {
            throw new IOException($"libVLC failed to load subtitle file: {Path.GetFileName(subtitlePath)}");
        }

        // Retrieve the newly-added SPU track id — it will be the highest-numbered SPU track.
        int newTrackId = _mediaPlayer.Spu;

        _logger.LogInformation("LoadExternalSubtitleAsync: loaded '{Path}', active spu={TrackId}.", subtitlePath, newTrackId);

        // Refresh metadata so the subtitle drawer shows the new track immediately.
        if (_mediaPlayer.Media is not null)
        {
            var updatedMeta = BuildMetadata(_mediaPlayer.Media);
            Metadata = updatedMeta;
            TracksChanged?.Invoke(this, new PlaybackTracksChangedEventArgs(updatedMeta));
        }

        return Task.FromResult(newTrackId);
    }

    public Task SetSubtitleDelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        _subtitleDelay = PlaybackConstraints.ClampSubtitleDelay(delay);
        // libVLC SetSpuDelay expects delay in microseconds (1 ms = 1000 µs)
        long microseconds = (long)(_subtitleDelay.TotalMilliseconds * 1000.0);
        _mediaPlayer.SetSpuDelay(microseconds);
        _logger.LogDebug("SetSubtitleDelayAsync: requested {Delay}ms -> clamped {Clamped}ms ({Microseconds}µs).",
            delay.TotalMilliseconds, _subtitleDelay.TotalMilliseconds, microseconds);
        return Task.CompletedTask;
    }

    public Task SetAudioDelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        _audioDelay = PlaybackConstraints.ClampAudioDelay(delay);
        // libVLC SetAudioDelay expects delay in microseconds (1 ms = 1000 µs)
        long microseconds = (long)(_audioDelay.TotalMilliseconds * 1000.0);
        _mediaPlayer.SetAudioDelay(microseconds);
        _logger.LogDebug("SetAudioDelayAsync: requested {Delay}ms -> clamped {Clamped}ms ({Microseconds}µs).",
            delay.TotalMilliseconds, _audioDelay.TotalMilliseconds, microseconds);
        return Task.CompletedTask;
    }

    public Task SetEqualizerEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        _equalizerConfig = _equalizerConfig with { IsEnabled = enabled };
        ApplyEqualizerToMediaPlayer();
        _logger.LogDebug("SetEqualizerEnabledAsync: {Enabled}.", enabled);
        return Task.CompletedTask;
    }

    public Task SetEqualizerPresetAsync(string presetName, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(presetName);
        cancellationToken.ThrowIfCancellationRequested();

        var preset = EqualizerPresets.GetPresetOrDefault(presetName);
        _equalizerConfig = _equalizerConfig with
        {
            PreampGainDb = preset.PreampGainDb,
            BandGainsDb = preset.BandGainsDb,
            PresetName = preset.Name,
        };
        ApplyEqualizerToMediaPlayer();
        _logger.LogDebug("SetEqualizerPresetAsync: applied '{Preset}'.", preset.Name);
        return Task.CompletedTask;
    }

    public Task SetEqualizerBandGainAsync(uint bandIndex, float gainDb, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (bandIndex >= _equalizerConfig.BandGainsDb.Count)
        {
            _logger.LogWarning("SetEqualizerBandGainAsync: band {Band} out of range; ignoring.", bandIndex);
            return Task.CompletedTask;
        }

        var bands = _equalizerConfig.BandGainsDb.ToArray();
        bands[bandIndex] = PlaybackConstraints.ClampEqualizerGain(gainDb);
        _equalizerConfig = _equalizerConfig with { BandGainsDb = bands, PresetName = "Custom" };
        ApplyEqualizerToMediaPlayer();
        _logger.LogDebug("SetEqualizerBandGainAsync: band {Band} -> {Gain}dB.", bandIndex, bands[bandIndex]);
        return Task.CompletedTask;
    }

    public Task SetEqualizerPreampAsync(float preampDb, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        _equalizerConfig = _equalizerConfig with { PreampGainDb = PlaybackConstraints.ClampEqualizerPreamp(preampDb) };
        ApplyEqualizerToMediaPlayer();
        _logger.LogDebug("SetEqualizerPreampAsync: {Preamp}dB.", _equalizerConfig.PreampGainDb);
        return Task.CompletedTask;
    }

    public Task ResetEqualizerAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        _equalizerConfig = EqualizerConfiguration.Default;
        ApplyEqualizerToMediaPlayer();
        _logger.LogDebug("ResetEqualizerAsync.");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Pushes <see cref="_equalizerConfig"/> onto the native libVLC equalizer. libVLC does not
    /// keep a reference to the <see cref="LibVLCSharp.Shared.Equalizer"/> handle after
    /// <see cref="LibVLCSharp.Shared.MediaPlayer.SetEqualizer"/> returns, so it is disposed
    /// immediately after applying (per the LibVLCSharp API contract).
    /// </summary>
    private void ApplyEqualizerToMediaPlayer()
    {
        if (!_equalizerConfig.IsEnabled)
        {
            _mediaPlayer.UnsetEqualizer();
            EqualizerChanged?.Invoke(this, new PlaybackEqualizerChangedEventArgs(_equalizerConfig));
            return;
        }

        using var equalizer = new LibVLCSharp.Shared.Equalizer();
        equalizer.SetPreamp(_equalizerConfig.PreampGainDb);
        for (uint band = 0; band < _equalizerConfig.BandGainsDb.Count; band++)
        {
            equalizer.SetAmp(_equalizerConfig.BandGainsDb[(int)band], band);
        }

        _mediaPlayer.SetEqualizer(equalizer);
        EqualizerChanged?.Invoke(this, new PlaybackEqualizerChangedEventArgs(_equalizerConfig));
    }

    public IReadOnlyList<AudioOutputDeviceInfo> GetAudioOutputDevices()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _mediaPlayer.AudioOutputDeviceEnum
            .Select(d => new AudioOutputDeviceInfo(d.DeviceIdentifier, d.Description))
            .ToList();
    }

    public Task SetAudioOutputDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        cancellationToken.ThrowIfCancellationRequested();

        // module=null is the recommended libVLC usage — moves the current output module's device.
        _mediaPlayer.SetOutputDevice(deviceId, null!);
        _activeAudioOutputDeviceId = deviceId;
        _logger.LogDebug("SetAudioOutputDeviceAsync: {DeviceId} (takes effect on next stop/restart).", deviceId);
        return Task.CompletedTask;
    }

    public Task SetAudioChannelAsync(AudioChannelMode mode, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        _mediaPlayer.SetChannel(ToVlcChannel(mode));
        _audioChannel = mode;
        _logger.LogDebug("SetAudioChannelAsync: {Mode}.", mode);
        return Task.CompletedTask;
    }

    public Task SetAudioEnhancementsAsync(AudioEnhancementOptions options, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        _audioEnhancements = options;
        _logger.LogDebug("SetAudioEnhancementsAsync: Normalization={Norm}, NightMode={Night} (applies on next load).",
            options.LoudnessNormalizationEnabled, options.NightModeEnabled);
        return Task.CompletedTask;
    }

    public Task SetSubtitleStyleAsync(SubtitleStyleOptions style, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(style);
        cancellationToken.ThrowIfCancellationRequested();

        _subtitleStyle = style with
        {
            TextScalePercent = PlaybackConstraints.ClampSubtitleTextScale(style.TextScalePercent),
            TextColorArgb = ClampColor(style.TextColorArgb),
            OutlineColorArgb = ClampColor(style.OutlineColorArgb),
            VerticalMarginPx = PlaybackConstraints.ClampVerticalMarginPx(style.VerticalMarginPx),
        };
        _logger.LogDebug(
            "SetSubtitleStyleAsync: Scale={Scale}%, Font={Font}, Color={Color}, OutlineColor={OutlineColor}, " +
            "Outline={Outline}, Margin={Margin}px (applies on next load).",
            _subtitleStyle.TextScalePercent, _subtitleStyle.FontFamily, _subtitleStyle.TextColorArgb,
            _subtitleStyle.OutlineColorArgb, _subtitleStyle.Outline, _subtitleStyle.VerticalMarginPx);
        return Task.CompletedTask;
    }

    private static int? ClampColor(int? argb) =>
        argb is null ? null : PlaybackConstraints.ClampSubtitleColor(argb.Value);

    public Task AttachVideoSurfaceAsync(IVideoSurface surface, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(surface);
        cancellationToken.ThrowIfCancellationRequested();

        // Redundant attach of the same surface: nothing to do.
        if (ReferenceEquals(_attachedSurface, surface))
        {
            _logger.LogDebug("AttachVideoSurfaceAsync: same surface re-attached; no-op.");
            return Task.CompletedTask;
        }

        var swapChainOptions = surface.SwapChainOptions;

        // Audio-only media has no video to render, so re-attaching a video surface does not need a
        // renderer built against that surface's swap-chain options. Rebuilding the native player
        // here would tear down the active audio session and restart the music for zero benefit,
        // because there is no picture to route to the new surface anyway. A plain rebind keeps the
        // audio playing seamlessly at its exact position with no native risk; a later video load
        // rebuilds against the surface's options naturally (on that load's attach). This is the
        // fix for "returning to the player restarts the music from the beginning".
        bool isAudioOnlySession = !ReferenceEquals(Metadata, MediaMetadata.Empty) &&
                                  !string.IsNullOrEmpty(_loadedPath) &&
                                  Metadata.VideoTracks.Count == 0;
        bool needsSwapChainRebuild = swapChainOptions is { Length: > 0 } &&
            !SwapChainOptionsEqual(swapChainOptions, _activeSwapChainOptions) &&
            !isAudioOnlySession;

        // Rebinding to a surface that needs construction-time swap-chain options (the WinUI
        // VideoView case) requires a renderer built against those options. If the currently-held
        // renderer was not constructed with them, rebuild it before handing it to the surface.
        if (needsSwapChainRebuild)
        {
            RebuildRenderer(swapChainOptions);
        }

        // Detach any previously attached surface so it stops displaying our renderer.
        _attachedSurface?.ClearRenderer();
        _attachedSurface = surface;

        surface.SetRenderer(_mediaPlayer);
        _logger.LogInformation("AttachVideoSurfaceAsync: renderer bound to surface (swap-chain {Count} options).",
            swapChainOptions?.Length ?? 0);
        return Task.CompletedTask;
    }

    public Task DetachVideoSurfaceAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        _attachedSurface?.ClearRenderer();
        _attachedSurface = null;
        _logger.LogDebug("DetachVideoSurfaceAsync: surface detached.");
        return Task.CompletedTask;
    }

    public Task AttachVisualizerSourceAsync(IVisualizerAudioSource source, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();

        _visualizerSource = source;

        // libVLC audio callbacks (SetAudioFormatCallback / SetAudioCallbacks) must be registered
        // BEFORE a media starts playing, and rebuilding the native player while it is actively
        // playing races the audio thread (and stalls the media clock) — so we never rebuild to
        // attach mid-playback. When the player is not playing it is safe to apply the callbacks
        // immediately; when it IS playing we defer: the tap is registered on the next natural
        // renderer rebuild (e.g. the next LoadAsync), after which it takes effect. RebuildRenderer
        // calls ApplyVisualizerAudioCallbacks() whenever _visualizerSource != null.
        if (_state != PlaybackState.Playing)
        {
            try
            {
                ApplyVisualizerAudioCallbacks();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AttachVisualizerSourceAsync: failed to apply PCM tap callbacks.");
            }
        }
        else
        {
            _logger.LogInformation("AttachVisualizerSourceAsync: playback active; PCM tap deferred until the next renderer rebuild (next load).");
        }

        _logger.LogInformation("AttachVisualizerSourceAsync: PCM tap attached (speaker output is silenced while the tap is active, per ADR-0008).");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Detaches the visualizer audio source and restores normal engine audio output. Safe to
    /// call with no source attached. Never rebuilds the native player while it is actively
    /// playing (a mid-playback rebuild races the audio thread and stalls the media clock), so
    /// while playback is active the un-registration is deferred to the next renderer rebuild.
    /// </summary>
    public Task DetachVisualizerSourceAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (_visualizerSource is null)
        {
            _logger.LogDebug("DetachVisualizerSourceAsync: no tap attached; no-op.");
            return Task.CompletedTask;
        }

        // Clear the source's buffers and drop our reference. While the player is not playing we can
        // safely rebuild it to unregister the callbacks (restoring normal audio output); while it is
        // playing we leave the (now no-op) callbacks registered until the next rebuild, since there
        // is no public libVLC API to unregister them without rebuilding.
        _visualizerSource.Stop();
        _visualizerSource = null;

        if (_state != PlaybackState.Playing)
        {
            try
            {
                RebuildRenderer(null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DetachVisualizerSourceAsync: rebuild to unregister PCM tap failed.");
            }
            _logger.LogInformation("DetachVisualizerSourceAsync: PCM tap detached; normal audio output restored.");
        }
        else
        {
            _logger.LogInformation("DetachVisualizerSourceAsync: playback active; PCM tap un-registration deferred until the next renderer rebuild (next load). Speaker audio resumes at that point.");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Registers (or re-registers) the visualizer PCM tap on the currently held
    /// <see cref="LibVLCSharp.Shared.MediaPlayer"/>. Called on attach and from the renderer
    /// rebuild path so a swap-chain rebuild keeps the tap alive. When no source is attached the
    /// callbacks are left unregistered — the player uses its normal audio output.
    /// </summary>
    private void ApplyVisualizerAudioCallbacks()
    {
        if (_visualizerSource is null)
        {
            return;
        }

        UnregisterVisualizerAudioCallbacks();
        _audioSetupCb = OnAudioSetup;
        _audioPlayCb = OnAudioPlay;
        _mediaPlayer.SetAudioFormatCallback(_audioSetupCb, OnAudioCleanup);
        _mediaPlayer.SetAudioCallbacks(_audioPlayCb, OnAudioPause, OnAudioResume, OnAudioFlush, OnAudioDrain);
    }

    private void UnregisterVisualizerAudioCallbacks()
    {
        // No public libVLC API to unregister callbacks; clearing only happens via rebuild
        // (RebuildRendererCore). The delegate references are dropped here so the previous
        // native player's registration no longer references captured engine state.
        _audioSetupCb = null;
        _audioPlayCb = null;
    }

    /// <summary>libVLC audio-setup callback: negotiate FL32 decoded PCM and capture rate/channels.</summary>
    private int OnAudioSetup(ref IntPtr opaque, ref IntPtr format, ref uint rate, ref uint channels)
    {
        // Write the "FL32" fourcc into libVLC's format buffer (IN/OUT contract of
        // libvlc_audio_set_format_callbacks). Accept libVLC's native rate and channel count —
        // never assume 44.1 kHz (PICKLE.md §34).
        _visualizerSampleRate = rate;
        _visualizerChannels = channels;

        // The setup callback runs on libVLC's audio thread, which keeps executing against the
        // (possibly stale) MediaPlayer while we attach/detach or rebuild on the UI thread. libVLC
        // may also call setup with a NULL/oversized pointer. Guard the write so a bad `format`
        // pointer can never throw an AccessViolationException that takes down the whole process;
        // on failure return non-zero so libVLC falls back to its default audio format.
        if (format == IntPtr.Zero)
        {
            _logger.LogWarning("OnAudioSetup: libVLC passed a NULL format pointer; falling back to default audio format.");
            return 1;
        }

        try
        {
            const string fourcc = "FL32";
            for (int i = 0; i < fourcc.Length && i < 4; i++)
            {
                Marshal.WriteByte(format, i, (byte)fourcc[i]);
            }
        }
        catch (Exception ex)
        {
            // Never crash the native audio thread — fall back to libVLC's default format.
            _logger.LogWarning(ex, "OnAudioSetup: failed to write FL32 format to libVLC's buffer; falling back to default audio format.");
            return 1;
        }
        return 0;
    }

    /// <summary>libVLC audio-play callback: marshal the decoded PCM to our source (native thread).</summary>
    private void OnAudioPlay(IntPtr data, IntPtr samples, uint count, long pts)
    {
        var source = _visualizerSource;
        if (source is null || count == 0 || _visualizerChannels == 0 || samples == IntPtr.Zero)
        {
            return;
        }

        try
        {
            int floatCount = (int)Math.Min(count * _visualizerChannels, int.MaxValue / 4);
            var span = new float[floatCount];
            Marshal.Copy(samples, span, 0, floatCount);
            source.PushSamples(span, (int)_visualizerSampleRate, (int)_visualizerChannels);
        }
        catch (Exception ex)
        {
            // The tap must never crash the native audio thread or abort playback.
            _logger.LogWarning(ex, "Visualizer PCM tap failed to push a batch.");
        }
    }

    private void OnAudioPause(IntPtr data, long pts)
    {
        _visualizerSource?.Stop();
    }

    private void OnAudioResume(IntPtr data, long pts)
    {
        // Nothing to do — the source accepts samples whenever they arrive.
    }

    private void OnAudioFlush(IntPtr data, long pts)
    {
        _visualizerSource?.Stop();
    }

    private void OnAudioDrain(IntPtr data)
    {
        _visualizerSource?.Stop();
    }

    private void OnAudioCleanup(IntPtr data)
    {
        // No resources to release; libVLC requires a cleanup callback for SetAudioFormatCallback.
    }

    /// <summary>Captures a still of the currently loaded media via libVLC's <c>video.takeSnapshot</c>.</summary>
    public Task<bool> TakeSnapshotAsync(string outputPath, uint width = 0, uint height = 0, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return Task.FromResult(false);
        }

        try
        {
            // LibVLCSharp MediaPlayer.TakeSnapshot(uint num, string path, uint width, uint height).
            // A zero width/height requests the native video size. Best-effort by contract: any
            // failure (no rendered frame, unwritable path, disabled VOUT) returns false, never throws.
            var ok = _mediaPlayer.TakeSnapshot(0, outputPath, width, height);
            if (!ok)
            {
                _logger.LogDebug("TakeSnapshotAsync: libVLC returned false for {Path}.", outputPath);
            }
            return Task.FromResult(ok);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TakeSnapshotAsync failed for {Path}.", outputPath);
            return Task.FromResult(false);
        }
    }

    private static bool SwapChainOptionsEqual(string[]? a, string[]? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        if (a.Length != b.Length) return false;
        for (var i = 0; i < a.Length; i++)
        {
            if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
        }
        return true;
    }

    /// <summary>
    /// Rebuilds <see cref="_mediaPlayer"/> (and its backing <see cref="LibVLCLib"/>) against a
    /// render target constructed with the given swap-chain options. Needed because libVLC's WinUI
    /// swap-chain VOUT must be constructed with <c>--winrt-swapchain</c>/<c>--winrt-d3dcontext</c>
    /// native pointers at LibVLC-construction time; the DI singleton was built without them.
    /// When <paramref name="swapChainOptions"/> is <see langword="null"/>, the currently-active
    /// swap-chain options (<see cref="_activeSwapChainOptions"/>) are reused — this is how the
    /// visualizer detach rebuilds the player (to unregister audio callbacks) without losing a
    /// bound video surface. Engine-owned settings (<see cref="_equalizerConfig"/>,
    /// <see cref="_audioEnhancements"/>, <see cref="_audioChannel"/>, <see cref="_subtitleStyle"/>,
    /// volume, mute, rate) are reapplied on the next <see cref="LoadAsync"/> / command, and the
    /// visualizer PCM tap is re-registered when one is attached, so both survive the rebuild.
    /// </summary>
    private void RebuildRenderer(string[]? swapChainOptions)
    {
        swapChainOptions ??= _activeSwapChainOptions;
        _logger.LogInformation("Rebuilding libVLC renderer (swap-chain options: {Count}).",
            swapChainOptions?.Length ?? 0);

        // State that must be carried to the replacement native player.
        var carryRate = _rate;
        var carryVolume = _volume;
        var carryMuted = _isMuted;
        var carryChannel = _audioChannel;
        var carryEqualizer = _equalizerConfig;

        var previous = _mediaPlayer;
        var oldLibVlc = _libVlc;
        var wasPlaying = _state == PlaybackState.Playing;
        var wasPaused = _state == PlaybackState.Paused;

        // Capture the current playback position BEFORE stopping the old player — Stop() clears it,
        // and the fresh player loads at position 0, so without this the rebuild lands the user at
        // the start of the file (the "returning to the player resets to the start and pauses" bug).
        long carryPositionMs = 0;
        if (wasPlaying || wasPaused)
        {
            try
            {
                carryPositionMs = previous.Time;
            }
            catch
            {
                carryPositionMs = 0;
            }
        }

        UnwireMediaPlayerEvents(previous);
        previous.Stop();

        var swapChainArgs = new List<string>(_options.ExtraArguments);
        if (swapChainOptions is not null)
        {
            swapChainArgs.AddRange(swapChainOptions);
        }

        // NOTE: offloading this construction to Task.Run was tried and reverted — it broke
        // VideoSurfaceTests/VisualizerPcmTapTests (rebuilt player got stuck in Buffering instead of
        // resuming Playing). LibVLC's native plugins (mmdevice/WASAPI, D3D11 video output) appear to
        // depend on being constructed on the same thread as the rest of this rebuild, so this stays
        // synchronous. See AGENTS.md for the still-open UI-thread-block issue this leaves.
        var newLibVlc = LibVlcBootstrap.CreateLibVlc(new LibVlcOptions
        {
            DisableAudio = _options.DisableAudio,
            Verbosity = _options.Verbosity,
            ExtraArguments = swapChainArgs.ToArray(),
        });
        var newPlayer = CreateMediaPlayer(newLibVlc);
        WireMediaPlayerEvents(newPlayer);

        // Swap to the new renderer. The old LibVLC/MediaPlayer are disposed only after the swap
        // so a failed native bootstrap leaves the engine in its previous working state.
        _libVlc = newLibVlc;
        _mediaPlayer = newPlayer;

        previous.Dispose();
        // Only dispose a LibVLC this engine created itself; the very first (injected) instance
        // is caller/DI-owned and must survive.
        if (_ownsLibVlc)
        {
            oldLibVlc.Dispose();
        }
        _ownsLibVlc = true;

        // Reapply engine-held settings to the fresh native player.
        _rate = carryRate;
        _volume = carryVolume;
        _isMuted = carryMuted;
        _audioChannel = carryChannel;
        _equalizerConfig = carryEqualizer;
        _mediaPlayer.SetRate((float)_rate);
        _mediaPlayer.Volume = _volume;
        _mediaPlayer.Mute = _isMuted;
        _mediaPlayer.SetChannel(ToVlcChannel(_audioChannel));
        ApplyEqualizerToMediaPlayer();
        if (_attachedSurface is not null)
        {
            _attachedSurface.SetRenderer(_mediaPlayer);
        }

        // The fresh MediaPlayer has no media loaded (LoadAsync set it on the pre-rebuild player), so
        // a later Play() would return false on an empty input. Re-attach the currently-loaded media;
        // metadata/tracks stay cached in this engine from the original LoadAsync parse.
        var carryPosition = TimeSpan.FromMilliseconds(carryPositionMs);
        if (_loadedPath is not null)
        {
            if (TryBuildVlcMediaFromLoaded(newLibVlc, out var media) && media is not null)
            {
                using (media)
                {
                    ApplyMetadataNoNetworkAccess(media);
                    ApplyAudioEnhancementsToMedia(media);
                    ApplySubtitleStyleToMedia(media);
                    _mediaPlayer.Media = media;
                }
            }
        }

        // Re-register the visualizer PCM tap on the fresh player if one is still attached.
        if (_visualizerSource is not null)
        {
            ApplyVisualizerAudioCallbacks();
        }

        _activeSwapChainOptions = swapChainOptions;

        // Best-effort carry of the captured playback position onto the fresh renderer, so a rebuild
        // (which the swap-chain VideoView forces on every re-entry to the player page) does not land
        // the user at the start of the file. Setting the fractional position BEFORE playback begins is
        // the only restoration mechanism that does not crash this engine: pre-Play Position is silently
        // dropped by libVLC once the fresh media starts parsing (so it may read back at 0), and both
        // the ":start-time" media option and a deferred Time set inside the Playing event reproduced a
        // native host crash/teardown (see AGENTS.md) — those are deliberately not used. This is thus a
        // best-effort that improves the common case when libVLC honours it, and degrades to a start
        // restart otherwise (never a crash).
        if ((wasPlaying || wasPaused) && carryPositionMs > 0)
        {
            var duration = Duration;
            try
            {
                if (duration > TimeSpan.Zero)
                {
                    _mediaPlayer.Position = (float)(carryPositionMs / duration.TotalMilliseconds);
                }
                else
                {
                    _mediaPlayer.Time = carryPositionMs;
                }
            }
            catch
            {
                // Position restore is best-effort; a fault must never break the rebuilt renderer.
            }
            PositionChanged?.Invoke(this, new PlaybackPositionChangedEventArgs(carryPosition));
        }

        // previous.Stop() above had its native Stopped event deliberately unwired first (a renderer
        // rebuild isn't a user-initiated stop), so the state machine never observed the old player
        // actually halting. When the rebuild interrupted genuinely active playback we must reconcile
        // the reported state — but instead of landing the user stopped-at-0, preserve continuity: a
        // Playing session resumes (the native Playing event transitions the state machine back to
        // Playing), and a Paused session rests paused. Resuming is gated on the visualizer PCM tap NOT
        // being attached — attaching the tap means the audio-callback registration path that produced
        // an uncatchable AccessViolationException was just touched, so we avoid immediately Play()-ing
        // into that same native-adjacent sequence (see AGENTS.md).
        if ((wasPlaying || wasPaused) && _visualizerSource is null)
        {
            try
            {
                if (!_mediaPlayer.Play())
                {
                    SetState(PlaybackState.Error);
                    var detail = BuildFailureContext();
                    ErrorOccurred?.Invoke(this, new PlaybackErrorEventArgs(
                        "libVLC MediaPlayer.Play() returned false after renderer rebuild." +
                        (string.IsNullOrEmpty(detail) ? "" : " " + detail), null));
                }
                else if (wasPaused && !wasPlaying)
                {
                    // A paused session should stay paused after the rebuild, not resume: start then
                    // immediately pause; the native Paused event lands the state machine on Paused.
                    _mediaPlayer.SetPause(true);
                }
            }
            catch
            {
                SetState(PlaybackState.Stopped);
            }
        }
        else if (wasPaused || (wasPlaying && _visualizerSource is not null))
        {
            // A paused session stays paused; a playing session with the tap attached must not be
            // started (crash safety), so it too rests paused — the UI shows "Play" truthfully rather
            // than a stale "Pause".
            SetState(PlaybackState.Paused);
        }
    }

    private LibVLCSharp.Shared.MediaPlayer CreateMediaPlayer(LibVLCLib libVlc) => new(libVlc);

    private void WireMediaPlayerEvents(LibVLCSharp.Shared.MediaPlayer player)
    {
        player.Playing += OnVlcPlaying;
        player.Paused += OnVlcPaused;
        player.Stopped += OnVlcStopped;
        player.EndReached += OnVlcEndReached;
        player.EncounteredError += OnVlcEncounteredError;
        player.TimeChanged += OnVlcTimeChanged;
        player.Buffering += OnVlcBuffering;
    }

    private void UnwireMediaPlayerEvents(LibVLCSharp.Shared.MediaPlayer player)
    {
        player.Playing -= OnVlcPlaying;
        player.Paused -= OnVlcPaused;
        player.Stopped -= OnVlcStopped;
        player.EndReached -= OnVlcEndReached;
        player.EncounteredError -= OnVlcEncounteredError;
        player.TimeChanged -= OnVlcTimeChanged;
        player.Buffering -= OnVlcBuffering;
    }

    private static LibVLCSharp.Shared.AudioOutputChannel ToVlcChannel(AudioChannelMode mode) => mode switch
    {
        AudioChannelMode.Stereo => LibVLCSharp.Shared.AudioOutputChannel.Stereo,
        AudioChannelMode.MonoLeft => LibVLCSharp.Shared.AudioOutputChannel.Left,
        AudioChannelMode.MonoRight => LibVLCSharp.Shared.AudioOutputChannel.Right,
        AudioChannelMode.ReverseStereo => LibVLCSharp.Shared.AudioOutputChannel.RStereo,
        AudioChannelMode.Dolby => LibVLCSharp.Shared.AudioOutputChannel.Dolbys,
        _ => LibVLCSharp.Shared.AudioOutputChannel.Stereo,
    };

    /// <summary>
    /// Applies loudness normalization (<c>normvol</c>) and night mode (<c>compressor</c>, libVLC's
    /// built-in dynamic range compressor with default parameters) as a per-media audio-filter
    /// chain. libVLC audio filters are configured on the <see cref="LibVLCSharp.Shared.Media"/>
    /// before playback starts, not hot-swappable on an already-loaded item.
    /// </summary>
    /// <summary>
    /// Applies per-media options that prevent libVLC from performing online metadata and cover-art
    /// lookups during <c>Media.Parse</c>. Without these, opening a local video that triggers art
    /// fetching makes the native parse hang inside libVLC's network meta/art fetchers
    /// (00_musicbrainz / 01_googleimage / 02_frenchtv / 03_lastfm Lua scripts) — blocked far past the
    /// parse timeout, stalling the whole load (a "freezes before any picture" symptom). Art is not
    /// needed at load time (we generate cover art ourselves via engine snapshots), so these options
    /// keep parse local, fast and deterministic.
    /// </summary>
    private static void ApplyMetadataNoNetworkAccess(LibVLCSharp.Shared.Media media)
    {
        media.AddOption(":no-metadata-network-access");
        media.AddOption(":no-arts");
    }

    private void ApplyAudioEnhancementsToMedia(LibVLCSharp.Shared.Media media)
    {
        var filters = new List<string>();
        if (_audioEnhancements.LoudnessNormalizationEnabled) filters.Add("normvol");
        if (_audioEnhancements.NightModeEnabled) filters.Add("compressor");
        if (filters.Count > 0)
        {
            media.AddOption($":audio-filter={string.Join(':', filters)}");
        }
    }

    /// <summary>
    /// Applies the subtitle style via libVLC per-media options on the
    /// <see cref="LibVLCSharp.Shared.Media"/> before it is assigned to the player:
    /// <c>sub-text-scale</c> (size), <c>freetype-font</c> (family) and the freetype text renderer
    /// styling vars for text color, outline color and outline thickness, plus <c>sub-margin</c>
    /// for vertical position. Only non-default values are emitted. These apply to plain-text
    /// subtitle formats (SRT/WebVTT); rich-text (ASS) and bitmap subtitles render their own style.
    /// </summary>
    private void ApplySubtitleStyleToMedia(LibVLCSharp.Shared.Media media)
    {
        var style = _subtitleStyle;
        var @default = SubtitleStyleOptions.Default;

        if (style.TextScalePercent != @default.TextScalePercent)
        {
            media.AddOption($":sub-text-scale={style.TextScalePercent}");
        }

        if (!string.IsNullOrWhiteSpace(style.FontFamily))
        {
            media.AddOption($":freetype-font={style.FontFamily}");
        }

        if (style.TextColorArgb is int textColor)
        {
            media.AddOption($":freetype-color={textColor}");
        }

        if (style.OutlineColorArgb is int outlineColor)
        {
            media.AddOption($":freetype-outline-color={outlineColor}");
        }

        if (style.Outline != @default.Outline)
        {
            media.AddOption($":freetype-outline-thickness={(int)style.Outline}");
        }

        if (style.VerticalMarginPx != @default.VerticalMarginPx)
        {
            media.AddOption($":sub-margin={style.VerticalMarginPx}");
        }
    }

    private static readonly HashSet<string> SubtitleExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".srt", ".ass", ".ssa", ".vtt", ".sub", ".smi", ".idx", ".txt" };

    private static string? ResolvePath(MediaFile media)
    {
        try
        {
            return Path.GetFullPath(media.FilePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>
    /// Builds a libVLC <see cref="Media"/> for the currently loaded source — either a local file
    /// path or a remote stream URI (streams are openable only when a genuine network source was
    /// loaded, never via the local-file rebuild path). Returns false and a null media when
    /// nothing usable is loaded, so the fresh player is simply left without media.
    /// </summary>
    private bool TryBuildVlcMediaFromLoaded(LibVLCLib libVlc, out LibVLCSharp.Shared.Media? media)
    {
        media = null;
        var loaded = _loadedPath;
        if (loaded is null)
        {
            return false;
        }

        try
        {
            if (System.Uri.TryCreate(loaded, System.UriKind.Absolute, out var uri) &&
                uri.Scheme is "http" or "https" or "rtsp" or "rtmp" or "mms" or "udp" or "ftp")
            {
                media = new LibVLCSharp.Shared.Media(libVlc, uri);
                return true;
            }
        }
        catch
        {
            // Fall through to the local-file path below.
        }

        if (!File.Exists(loaded))
        {
            return false;
        }

        media = new LibVLCSharp.Shared.Media(libVlc, loaded);
        return true;
    }

    /// <summary>
    /// Fast media header and integrity pre-check. Reads the initial bytes and rejects files
    /// whose header matches none of the supported container signatures, or whose size is so
    /// small that libVLC's demuxer triggers an unhandled native access violation (SEH c0000005)
    /// trying to seek beyond EOF. Genuine media files pass through to libVLC's full demuxer.
    /// </summary>
    private static bool HasRecognisedMediaHeader(string path)
    {
        try
        {
            var fileInfo = new FileInfo(path);
            // Any container-based media smaller than 4 KB is severely truncated and unsafe for libVLC demuxers.
            if (fileInfo.Length < 4096)
            {
                // Pure audio files (MP3, WAV) can be tiny — allow them if they have valid audio headers.
                Span<byte> audioHeader = stackalloc byte[4];
                using var afs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4, FileOptions.SequentialScan);
                var audioRead = afs.Read(audioHeader);
                if (audioRead >= 3 && audioHeader[0] == 'I' && audioHeader[1] == 'D' && audioHeader[2] == '3') return true;
                if (audioRead >= 2 && audioHeader[0] == 0xFF && (audioHeader[1] & 0xE0) == 0xE0) return true;
                if (audioRead >= 4 && audioHeader[0] == 'R' && audioHeader[1] == 'I' && audioHeader[2] == 'F' && audioHeader[3] == 'F') return true;
                if (audioRead >= 4 && audioHeader[0] == 'f' && audioHeader[1] == 'L' && audioHeader[2] == 'a' && audioHeader[3] == 'C') return true;
                if (audioRead >= 4 && audioHeader[0] == 'O' && audioHeader[1] == 'g' && audioHeader[2] == 'g' && audioHeader[3] == 'S') return true;
                return false;
            }

            Span<byte> header = stackalloc byte[12];
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 12, FileOptions.SequentialScan);
            var read = fs.Read(header);
            if (read < 4) return false;

            // ISO Base Media (MP4, M4A, M4V, MOV): bytes 4-7 = "ftyp" / "moov" / "mdat" / "free" / "wide"
            if (read >= 8 && header[4] == 'f' && header[5] == 't' && header[6] == 'y' && header[7] == 'p') return true;
            if (read >= 8 && header[4] == 'm' && header[5] == 'o' && header[6] == 'o' && header[7] == 'v') return true;

            // RIFF (AVI, WAV): 0-3 = "RIFF"
            if (header[0] == 'R' && header[1] == 'I' && header[2] == 'I' && header[3] == 'F') return true;

            // EBML / MKV / WebM: 0-3 = 0x1A 0x45 0xDF 0xA3
            if (header[0] == 0x1A && header[1] == 0x45 && header[2] == 0xDF && header[3] == 0xA3) return true;

            // MPEG-TS: sync byte 0x47 at offset 0 (or 4 for M2TS with 4-byte timestamp prefix)
            if (header[0] == 0x47) return true;
            if (read >= 5 && header[4] == 0x47) return true;

            // MPEG PS / VOB: 0-3 = 0x00 0x00 0x01 0xBA
            if (header[0] == 0x00 && header[1] == 0x00 && header[2] == 0x01 && header[3] == 0xBA) return true;

            // MP3: ID3 tag or sync word (0xFF 0xFB / 0xFF 0xF3 / 0xFF 0xF2 / 0xFF 0xFA)
            if (header[0] == 'I' && header[1] == 'D' && header[2] == '3') return true;
            if (header[0] == 0xFF && (header[1] & 0xE0) == 0xE0) return true;

            // FLAC: 0-3 = "fLaC"
            if (header[0] == 'f' && header[1] == 'L' && header[2] == 'a' && header[3] == 'C') return true;

            // OGG (OGV, Opus, Vorbis): 0-3 = "OggS"
            if (header[0] == 'O' && header[1] == 'g' && header[2] == 'g' && header[3] == 'S') return true;

            // ASF / WMV / WMA: GUID 30 26 B2 75
            if (header[0] == 0x30 && header[1] == 0x26 && header[2] == 0xB2 && header[3] == 0x75) return true;

            // FLV: 0-2 = "FLV"
            if (header[0] == 'F' && header[1] == 'L' && header[2] == 'V') return true;

            return false;
        }
        catch
        {
            // Any I/O error: let libVLC try and fail gracefully via status check.
            return true;
        }
    }

    private void TransitionToLoading()
    {
        // Loading is always legal: it stops any current media and replaces it.
        SetState(PlaybackState.Loading);
    }

    // ── libVLC event handlers (called on native threads) ─────────────────────

    private void OnVlcPlaying(object? sender, EventArgs e)
    {
        _logger.LogDebug("libVLC event: Playing");
        SetState(PlaybackState.Playing);
    }

    private void OnVlcBuffering(object? sender, LibVLCSharp.Shared.MediaPlayerBufferingEventArgs e)
    {
        _logger.LogDebug("libVLC event: Buffering {Percent}%", e.Cache);
        var next = EvaluateBuffering(_state, e.Cache);
        if (next.HasValue) SetState(next.Value);
    }

    /// <summary>
    /// Decides what a libVLC <c>Buffering</c> event (raised on a native thread) should do to the state
    /// machine. libVLC reports a cache percentage 0–100: values < 100 are a genuine pre-roll/stall
    /// (surface <see cref="PlaybackState.Buffering"/>), but a full-buffer event (cache ≥ 100) is ALSO
    /// re-raised periodically throughout active playback without a matching <c>Playing</c> re-fire.
    /// An unconditional transition would therefore wedge the machine below <see cref="PlaybackState.Playing"/>
    /// mid-playback — play button stuck showing "play", Pause unreachable, UI chip reading "Buffering".
    /// A full-buffer event must never demote an already Playing/Paused session; it only lifts a pending
    /// Buffering back to Playing. Returns null when the state must not change.
    /// </summary>
    internal static PlaybackState? EvaluateBuffering(PlaybackState current, float cache)
    {
        if (cache < 100)
        {
            return PlaybackState.Buffering;
        }
        return current == PlaybackState.Buffering ? PlaybackState.Playing : (PlaybackState?)null;
    }

    private void OnVlcPaused(object? sender, EventArgs e)
    {
        _logger.LogDebug("libVLC event: Paused");
        SetState(PlaybackState.Paused);
    }

    private void OnVlcStopped(object? sender, EventArgs e)
    {
        _logger.LogDebug("libVLC event: Stopped");
        SetState(PlaybackState.Stopped);
    }

    private void OnVlcEndReached(object? sender, EventArgs e)
    {
        _logger.LogDebug("libVLC event: EndReached at {Position}ms", _mediaPlayer.Time);
        var finalPosition = Position;
        SetState(PlaybackState.Stopped);
        EndReached?.Invoke(this, new PlaybackEndedEventArgs(finalPosition));
    }

    private void OnVlcEncounteredError(object? sender, EventArgs e)
    {
        _logger.LogError("libVLC event: EncounteredError");

        // Phase 8 reconnect: for a remote stream that was actively playing/buffering, absorb a
        // transient drop and retry a bounded number of times on the existing player (safe reload,
        // no native rebuild) before surfacing a hard error.
        if (TryBeginAutoReconnect())
        {
            return;
        }

        var detail = BuildFailureContext();
        var err = new InvalidOperationException(
            "Stream/playback error." +
            (string.IsNullOrEmpty(detail) ? "" : " " + detail));
        SetState(PlaybackState.Error);
        ErrorOccurred?.Invoke(this, new PlaybackErrorEventArgs(err.Message, err));
    }

    /// <summary>
    /// Begins a bounded, fire-and-forget auto-reconnect of a dropped remote stream. Returns true
    /// when a reconnect attempt has been scheduled (the hard error is deferred), false when the
    /// current connection is not reconnectable (local media, already reconnecting, or retries
    /// exhausted) — in which case the caller should surface the error normally.
    /// </summary>
    private bool TryBeginAutoReconnect()
    {
        MediaFile? media;
        lock (_reconnectLock)
        {
            // Only remote streams reconnect; a local file error is a genuine problem to surface.
            media = _loadedMedia;
            if (media is not { IsRemoteUri: true })
            {
                return false;
            }

            // Don't stack another reconnect while one is already (re)loading.
            if (_reconnectCts is not null)
            {
                return false;
            }

            _reconnectAttempt++;
            _reconnectCts = new CancellationTokenSource();
        }

        _ = Task.Run(() => AutoReconnectAsync(media, _reconnectCts.Token));
        return true;
    }

    private async Task AutoReconnectAsync(MediaFile media, CancellationToken token)
    {
        _reconnecting = true;
        var startAttempt = _reconnectAttempt;
        var position = Position;
        Exception? lastError = null;

        try
        {
            for (var attempt = startAttempt; attempt <= MaxReconnectAttempts; attempt++)
            {
                var backoff = ReconnectBackoff[Math.Min(attempt - 1, ReconnectBackoff.Length - 1)];
                _logger.LogWarning("Auto-reconnecting dropped stream '{Uri}' (attempt {Attempt}/{Max}).",
                    media.FilePath, attempt, MaxReconnectAttempts);

                try
                {
                // Signal the reconnect via the transient Buffering state (the UI chip reads it).
                SetState(PlaybackState.Buffering);
                await Task.Delay(backoff, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();

                await LoadAsync(media, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();

                if (_state != PlaybackState.Ready)
                {
                    // LoadAsync swallowed a failure into the Error state; treat as one failed retry.
                    lastError = new InvalidOperationException("Reconnect reload did not reach Ready.");
                    continue;
                }

                await PlayAsync(token).ConfigureAwait(false);

                // Best-effort restore of the prior position on the fresh stream; never fatal.
                try
                {
                    if (_state == PlaybackState.Playing && position > TimeSpan.Zero && Duration > TimeSpan.Zero)
                    {
                        _mediaPlayer.Position = (float)(position.TotalMilliseconds / Duration.TotalMilliseconds);
                    }
                }
                catch
                {
                }

                _logger.LogInformation("Auto-reconnect succeeded for '{Uri}'.", media.FilePath);
                return;
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Auto-reconnect cancelled.");
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
                _logger.LogError(ex, "Auto-reconnect attempt {Attempt} failed.", attempt);
            }
        }

        // All retries exhausted — surface the failure as the normal hard Error state.
            if (_state != PlaybackState.Error)
            {
                var err = lastError ?? new InvalidOperationException("Stream failed after reconnection attempts.");
                SetState(PlaybackState.Error);
                ErrorOccurred?.Invoke(this, new PlaybackErrorEventArgs(
                    $"Could not reconnect to stream '{media.FilePath}'.", err));
            }
        }
        finally
        {
            _reconnecting = false;
            CancelAutoReconnect();
        }
    }

    private void CancelAutoReconnect()
    {
        lock (_reconnectLock)
        {
            _reconnectCts?.Cancel();
            _reconnectCts?.Dispose();
            _reconnectCts = null;
        }
    }

    private void OnVlcTimeChanged(object? sender, LibVLCSharp.Shared.MediaPlayerTimeChangedEventArgs e)
    {
        if (e.Time <= 0) return;
        PositionChanged?.Invoke(this, new PlaybackPositionChangedEventArgs(TimeSpan.FromMilliseconds(e.Time)));
    }

    // Surfaces the deep libVLC reason for a Play/EncounteredError failure directly in the message
    // shown on-screen, instead of relying on the user to fetch the log file. Pulls the tail of the
    // recent libVLC log, keeps only plausible failure-signal lines, and returns them joined.
    private static string BuildFailureContext()
    {
        try
        {
            var lines = LibVlcBootstrap.GetRecentLogLines();
            if (lines.Count == 0)
            {
                return string.Empty;
            }

            string[] signals =
            {
                "error", "failed", "no suitable", "cannot", "unable", "could not",
                "no input", "not found", "unsupported", "corrupt", "too many", "invalid",
            };

            var hits = lines
                .Where(l => l.IndexOf(": error ", StringComparison.OrdinalIgnoreCase) >= 0)
                .Union(lines.Where(l => signals.Any(s => l.Contains(s, StringComparison.OrdinalIgnoreCase))))
                .Take(8)
                .ToArray();

            return hits.Length == 0 ? string.Empty : "libVLC detail: " + string.Join(" | ", hits);
        }
        catch
        {
            return string.Empty;
        }
    }

    private void SetState(PlaybackState next)
    {
        if (_state == next)
        {
            return;
        }

        var previous = _state;
        _state = next;
        _logger.LogDebug("Playback state: {Previous} -> {Next}", previous, next);
        StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(previous, next));
    }

    private void FailLoad(Exception error)
    {
        _logger.LogError(error, "Media load failed.");
        Metadata = MediaMetadata.Empty;
        _mediaPlayer.Stop();
        SetState(PlaybackState.Error);
        ErrorOccurred?.Invoke(this, new PlaybackErrorEventArgs(error.Message, error));
    }

    private MediaMetadata BuildMetadata(LibVLCSharp.Shared.Media vlcMedia)
    {
        var tracks = vlcMedia.Tracks ?? [];
        return MetadataMapper.ToMetadata(vlcMedia.Duration, tracks);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Cancel any in-flight stream auto-reconnect so it can't reload a disposed player.
        CancelAutoReconnect();

        // Unsubscribe before Stop/Dispose so no events fire after disposal.
        _attachedSurface?.ClearRenderer();
        _attachedSurface = null;

        UnwireMediaPlayerEvents(_mediaPlayer);
        _mediaPlayer.Stop();
        _mediaPlayer.Dispose();
        // The initially-injected LibVLC is caller/DI-owned (shared singleton); only dispose
        // instances this engine created itself during a swap-chain renderer rebuild.
        if (_ownsLibVlc)
        {
            _libVlc.Dispose();
        }
        _logger.LogDebug("LibVLC playback engine disposed.");
    }
}