using System.ComponentModel;
using System.Runtime.CompilerServices;
using PlaySphere.Application.AI;
using PlaySphere.Application.Diagnostics;
using PlaySphere.Application.Library;
using PlaySphere.Application.Media;
using PlaySphere.Application.Preferences;
using PlaySphere.Domain.Library;
using PlaySphere.Domain.Media;
using PlaySphere.Domain.Playback;
using PlaySphere.Playback.Abstractions;
using PlaySphere.Subtitles;
using PlaySphere.UI.Models;

namespace PlaySphere.UI.ViewModels;

/// <summary>
/// ViewModel for <see cref="Pages.PlayerPage"/>.
/// Wraps <see cref="IMediaPlaybackEngine"/> and exposes bindable properties for
/// play/pause/stop, seek, volume, mute, rate, track enumeration, and slide-out drawers.
/// No libVLC refs here — strictly using the abstraction layer (PICKLE.md §13 architecture boundary).
/// </summary>
public sealed class PlayerViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly TimeSpan SkipAmount = TimeSpan.FromSeconds(10);

    private readonly IMediaPlaybackEngine _engine;
    private readonly IMediaLibraryRepository? _repository;
    private readonly string? _thumbnailDirectory;
    private readonly IPreferencesStore? _preferences;
    private readonly IMediaClipExporter? _clipExporter;
    private readonly IMediaConverter? _mediaConverter;

    private string _title = "Now Playing";
    private string? _filePath;
    private bool _isPlaying;
    private bool _isMuted;
    private bool _isSeeking;
    private bool _mediaEnded;
    private bool _disposed;
    private PlayerDrawerKind _activeDrawer = PlayerDrawerKind.None;
    private int? _selectedAudioTrackId;
    private int? _selectedSubtitleTrackId;
    private SubtitleDocument? _loadedSubtitleDocument;

    // Library-aware playback session (Continue Watching / resume, PICKLE.md §23/§26).
    private int? _libraryItemId;
    private LibraryMediaItem? _libraryItem;
    private TimeSpan _lastRecordedPosition = TimeSpan.MinValue;
    private DateTime _lastPeriodicRecordUtc = DateTime.MinValue;
    private bool _thumbnailCaptureStarted;
    private bool _autoSelectAudioDone;
    private IReadOnlyList<Bookmark> _bookmarks = [];
    private IReadOnlyList<AiChapterEntity> _chapters = [];
    private bool _isGeneratingChapters;
    private string? _chaptersMessage;
    private readonly IGenerateChaptersRunner? _chaptersRunner;

    // Playlist / queue playback session (PICKLE.md §25). Operates over media file paths, since the
    // engine loads by path; drives Play Next / Play Previous / shuffle / repeat over a collection.
    private PlaybackQueue<string>? _queue;
    private bool _isShuffled;
    private PlaybackRepeat _repeatMode = PlaybackRepeat.Off;

    // Mark In/Out clip editing state (PICKLE.md §1102). Marks are per-loaded-file; points only ever
    // describe an In < Out range (a mark that would invert the pair clears the older one).
    private TimeSpan? _markIn;
    private TimeSpan? _markOut;
    private bool _isConverting;
    private readonly IMediaThumbnailProvider? _thumbnailProvider;
    private readonly string? _thumbnailStripCacheDir;
    private ThumbnailStripResult? _thumbnailStrip;
    private readonly IWaveformProvider? _waveformProvider;
    private readonly string? _waveformCacheDir;
    private WaveformResult? _waveform;

    // Stereo balance (ADR-0011). Per-loaded-file, like marks/thumbnails: reset on every LoadAsync
    // and torn down on Dispose so the secondary audio path never outlives the file it was bound to.
    private readonly IStereoBalanceAudioSession? _stereoBalanceSession;
    private StereoBalance _stereoBalance = StereoBalance.Center;

    public PlayerViewModel(IMediaPlaybackEngine engine, IMediaLibraryRepository? repository = null, string? thumbnailDirectory = null, IPreferencesStore? preferences = null, IMediaClipExporter? clipExporter = null, IMediaConverter? mediaConverter = null, IMediaThumbnailProvider? thumbnailProvider = null, string? thumbnailStripCacheDir = null, IWaveformProvider? waveformProvider = null, string? waveformCacheDir = null, IGenerateChaptersRunner? generateChaptersRunner = null, IStereoBalanceAudioSession? stereoBalanceSession = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _engine = engine;
        _repository = repository;
        _thumbnailDirectory = thumbnailDirectory;
        _preferences = preferences;
        _clipExporter = clipExporter;
        _mediaConverter = mediaConverter;
        _thumbnailProvider = thumbnailProvider;
        _thumbnailStripCacheDir = thumbnailStripCacheDir;
        _waveformProvider = waveformProvider;
        _waveformCacheDir = waveformCacheDir;
        _chaptersRunner = generateChaptersRunner;
        _stereoBalanceSession = stereoBalanceSession;

        _engine.StateChanged += OnStateChanged;
        _engine.PositionChanged += OnPositionChanged;
        _engine.ErrorOccurred += OnErrorOccurred;
        _engine.TracksChanged += OnTracksChanged;
        _engine.EqualizerChanged += OnEqualizerChanged;
        _engine.EndReached += OnEngineEndReached;

        // This constructor is also used to re-attach to an already-loaded, already-playing engine
        // (e.g. returning to the player from elsewhere in the shell without reloading). The above
        // subscriptions only observe *future* changes, so sync the cached observable properties
        // (Title/FilePath/IsPlaying) from the engine's current state now — otherwise the page would
        // render as if nothing were loaded despite playback actually continuing underneath.
        if (_engine.LoadedFilePath is { } loadedPath)
        {
            _filePath = loadedPath;
            _title = MediaFile.FromPathOrUri(loadedPath).DisplayTitle;
        }
        _isPlaying = _engine.State == PlaybackState.Playing;
    }

    // ── INotifyPropertyChanged ──────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Notify([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // ── Observable properties ───────────────────────────────────────────────

    /// <summary>Human-readable state label for the UI status chip.</summary>
    public string StateLabel => _engine.State.ToString();

    private string? _lastError;

    /// <summary>The most recent playback failure message, or null when none (cleared on a
    /// successful load, play, or any transition out of the Error state).</summary>
    public string? LastError
    {
        get => _lastError;
        private set { if (_lastError != value) { _lastError = value; Notify(); } }
    }

    /// <summary>True while playback is Playing; drives Play/Pause button icon.</summary>
    public bool IsPlaying
    {
        get => _isPlaying;
        private set { if (_isPlaying != value) { _isPlaying = value; Notify(); } }
    }

    /// <summary>Current playback position as a fraction 0–100 for the timeline slider.</summary>
    public double PositionPercent
    {
        get
        {
            var dur = _engine.Duration;
            if (dur <= TimeSpan.Zero) return 0;
            return _engine.Position.TotalSeconds / dur.TotalSeconds * 100.0;
        }
    }

    /// <summary>Current position formatted as m:ss.</summary>
    public string PositionText => FormatTime(_engine.Position);

    /// <summary>Total duration formatted as m:ss.</summary>
    public string DurationText => FormatTime(_engine.Duration);

    /// <summary>Raw position TimeSpan.</summary>
    public TimeSpan Position => _engine.Position;

    /// <summary>Raw duration TimeSpan.</summary>
    public TimeSpan Duration => _engine.Duration;

    /// <summary>
    /// True when the loaded media can be seeked. False for live/interminable streams, for which
    /// the timeline and skip controls should be disabled (the UI shows a live indicator instead).
    /// </summary>
    public bool IsSeekable => _engine.IsSeekable;

    /// <summary>Current volume 0–100.</summary>
    public int Volume => _engine.Volume;

    /// <summary>True when muted.</summary>
    public bool IsMuted
    {
        get => _isMuted;
        private set { if (_isMuted != value) { _isMuted = value; Notify(); } }
    }

    /// <summary>Current playback rate.</summary>
    public double Rate => _engine.Rate;

    /// <summary>Current playback rate formatted as "1.0×".</summary>
    public string RateText => $"{_engine.Rate:F1}×";

    /// <summary>Title of the current media, or "Now Playing" when nothing is loaded.</summary>
    public string Title
    {
        get => _title;
        private set { if (_title != value) { _title = value; Notify(); } }
    }

    /// <summary>File path of current loaded media, if loaded from local disk.</summary>
    public string? FilePath
    {
        get => _filePath;
        private set { if (_filePath != value) { _filePath = value; Notify(); } }
    }

    /// <summary>
    /// The cataloged library item for the loaded file, when it's part of the media library
    /// (null otherwise). Exposes the FFprobe-populated technical metadata for the Media Info drawer.
    /// </summary>
    public LibraryMediaItem? LibraryItem => _libraryItem;

    /// <summary>Active slide-out drawer kind.</summary>
    public PlayerDrawerKind ActiveDrawer
    {
        get => _activeDrawer;
        private set
        {
            if (_activeDrawer != value)
            {
                _activeDrawer = value;
                Notify();
                Notify(nameof(IsDrawerOpen));
            }
        }
    }

    /// <summary>True when any drawer is currently open.</summary>
    public bool IsDrawerOpen => _activeDrawer != PlayerDrawerKind.None;

    /// <summary>Technical metadata probed for the current media item.</summary>
    public MediaMetadata Metadata => _engine.Metadata;

    /// <summary>List of audio streams available in the loaded media item.</summary>
    public IReadOnlyList<AudioTrackInfo> AudioTracks => _engine.Metadata.AudioTracks;

    /// <summary>List of subtitle tracks available in the loaded media item.</summary>
    public IReadOnlyList<SubtitleTrackInfo> SubtitleTracks => _engine.Metadata.SubtitleTracks;

    /// <summary>List of video tracks available in the loaded media item.</summary>
    public IReadOnlyList<VideoTrackInfo> VideoTracks => _engine.Metadata.VideoTracks;

    /// <summary>Selected audio track ID (null = disabled / engine default).</summary>
    public int? SelectedAudioTrackId
    {
        get => _selectedAudioTrackId;
        private set { if (_selectedAudioTrackId != value) { _selectedAudioTrackId = value; Notify(); } }
    }

    /// <summary>Selected subtitle track ID (null = off).</summary>
    public int? SelectedSubtitleTrackId
    {
        get => _selectedSubtitleTrackId;
        private set { if (_selectedSubtitleTrackId != value) { _selectedSubtitleTrackId = value; Notify(); } }
    }

    /// <summary>Current subtitle delay TimeSpan.</summary>
    public TimeSpan SubtitleDelay => _engine.SubtitleDelay;

    /// <summary>Current subtitle delay in milliseconds.</summary>
    public double SubtitleDelayMilliseconds => _engine.SubtitleDelay.TotalMilliseconds;

    /// <summary>Formatted subtitle delay string (e.g. "0 ms", "+150 ms", "-200 ms").</summary>
    public string SubtitleDelayText => FormatDelay(_engine.SubtitleDelay);

    /// <summary>Current audio delay TimeSpan.</summary>
    public TimeSpan AudioDelay => _engine.AudioDelay;

    /// <summary>Current audio delay in milliseconds.</summary>
    public double AudioDelayMilliseconds => _engine.AudioDelay.TotalMilliseconds;

    /// <summary>Formatted audio delay string (e.g. "0 ms", "+150 ms", "-200 ms").</summary>
    public string AudioDelayText => FormatDelay(_engine.AudioDelay);

    /// <summary>True when the 10-band equalizer filter is active.</summary>
    public bool IsEqualizerEnabled => _engine.IsEqualizerEnabled;

    /// <summary>Current equalizer configuration snapshot (preamp, band gains, active preset).</summary>
    public EqualizerConfiguration EqualizerConfig => _engine.EqualizerConfig;

    /// <summary>Available equalizer presets (e.g. "Flat", "Rock", "Pop").</summary>
    public IReadOnlyList<string> EqualizerPresetNames => EqualizerPresets.PresetNames;

    /// <summary>System audio output devices available for selection.</summary>
    public IReadOnlyList<AudioOutputDeviceInfo> AudioOutputDevices => _engine.GetAudioOutputDevices();

    /// <summary>Currently selected audio output device id, or null when unset.</summary>
    public string? ActiveAudioOutputDeviceId => _engine.ActiveAudioOutputDeviceId;

    /// <summary>Current stereo/mono downmix channel mode.</summary>
    public AudioChannelMode AudioChannel => _engine.AudioChannel;

    /// <summary>True when loudness normalization is enabled (applies on next media load).</summary>
    public bool IsLoudnessNormalizationEnabled => _engine.AudioEnhancements.LoudnessNormalizationEnabled;

    /// <summary>True when night mode (dynamic range compression) is enabled (applies on next media load).</summary>
    public bool IsNightModeEnabled => _engine.AudioEnhancements.NightModeEnabled;

    /// <summary>Current full subtitle rendering style.</summary>
    public SubtitleStyleOptions SubtitleStyle => _engine.SubtitleStyle;

    /// <summary>Current subtitle text scale, as a percent (25–500, default 100).</summary>
    public int SubtitleTextScalePercent => _engine.SubtitleStyle.TextScalePercent;

    /// <summary>Current subtitle vertical position as a bottom margin in pixels.</summary>
    public int SubtitleVerticalMarginPx => _engine.SubtitleStyle.VerticalMarginPx;

    /// <summary>Current subtitle font family, or null for the system default.</summary>
    public string? SubtitleFontFamily => _engine.SubtitleStyle.FontFamily;

    /// <summary>
    /// Current subtitle text color as 0xRRGGBB, or null for the default (white).
    /// </summary>
    public int? SubtitleTextColorArgb => _engine.SubtitleStyle.TextColorArgb;

    /// <summary>Current subtitle outline color as 0xRRGGBB, or null for the default (black).</summary>
    public int? SubtitleOutlineColorArgb => _engine.SubtitleStyle.OutlineColorArgb;

    /// <summary>Current subtitle outline thickness.</summary>
    public SubtitleOutlineStyle SubtitleOutline => _engine.SubtitleStyle.Outline;

    /// <summary>
    /// The parsed cue list for the currently loaded external subtitle, when its format is
    /// parseable (SRT/WebVTT), or null when no subtitle is loaded or its format can't be
    /// parsed for cue browsing (PICKLE.md §17 "cue search, cue list").
    /// </summary>
    public SubtitleDocument? LoadedSubtitleDocument
    {
        get => _loadedSubtitleDocument;
        private set { if (_loadedSubtitleDocument != value) { _loadedSubtitleDocument = value; Notify(); } }
    }

    /// <summary>True when a multi-item queue (playlist collection) is loaded for sequential playback.</summary>
    public bool IsQueueActive => _queue is { Count: > 0 };

    /// <summary>True when the current queue is shuffled.</summary>
    public bool IsShuffled
    {
        get => _isShuffled;
        private set { if (_isShuffled != value) { _isShuffled = value; Notify(); } }
    }

    /// <summary>Current repeat behavior of the queue.</summary>
    public PlaybackRepeat RepeatMode
    {
        get => _repeatMode;
        private set { if (_repeatMode != value) { _repeatMode = value; Notify(); Notify(nameof(RepeatModeLabel)); } }
    }

    /// <summary>Human-readable repeat label ("Off" / "One" / "All").</summary>
    public string RepeatModeLabel => RepeatMode switch
    {
        PlaybackRepeat.One => "Repeat One",
        PlaybackRepeat.All => "Repeat All",
        _ => "Repeat Off",
    };

    /// <summary>Queue position label such as "3 / 12", or empty when no queue is active.</summary>
    public string QueuePositionLabel
    {
        get
        {
            if (_queue is null || _queue.Count == 0)
            {
                return string.Empty;
            }

            var cursor = _queue.Cursor;
            int shown;
            if (cursor < 0)
            {
                shown = 0;
            }
            else if (cursor >= _queue.Count)
            {
                shown = _queue.Count;
            }
            else
            {
                shown = cursor + 1;
            }

            return $"{shown} / {_queue.Count}";
        }
    }

    /// <summary>True when there is a queue and it can move to a previous item.</summary>
    public bool HasPrevious => _queue is { Count: > 0 };

    /// <summary>True when the loaded queue has a next item to advance to (Off mode past the end reports false).</summary>
    public bool HasNext => _queue is { Count: > 0 };

    // ── Commands / actions ──────────────────────────────────────────────────

    /// <summary>Loads a media file and transitions to Ready. Auto-loads a matching subtitle file if present.</summary>
    public async Task LoadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ObjectDisposedException.ThrowIf(_disposed, this);
        MediaFile media = MediaFile.FromPathOrUri(filePath);
        FilePath = filePath;
        Title = media.DisplayTitle;
        LoadedSubtitleDocument = null;
        _autoSelectAudioDone = false;
        _mediaEnded = false;
        SelectedAudioTrackId = null;
        ClearMarks();
        ClearThumbnailStrip();
        ClearWaveform();
        await DisableStereoBalanceAsync().ConfigureAwait(false);
        await _engine.LoadAsync(media, cancellationToken).ConfigureAwait(false);
        NotifyAll();

        if (media.IsRemoteUri)
        {
            // Remote streams have no sibling subtitle files or library session; nothing further to wire.
            return;
        }

        // Automatically load a matching subtitle file if found alongside the media.
        await TryAutoLoadSubtitleAsync(cancellationToken).ConfigureAwait(false);

        // If the file is a cataloged library item, bind a playback session: remember its id so
        // progress is recorded (Continue Watching) and seek back to where the user last left off.
        await ResolveLibrarySessionAsync(cancellationToken).ConfigureAwait(false);

        // Best-effort timeline hover thumbnails (PICKLE.md §20) for local, seekable media.
        _ = PrepareThumbnailStripAsync();

        // Best-effort timeline waveform (PICKLE.md §20) for local, seekable media with audio.
        _ = PrepareWaveformAsync();
    }

    /// <summary>
    /// Loads a queue of media file paths for sequential playback (PICKLE.md §25 "queue", "play next").
    /// If <paramref name="startFilePath"/> is present in <paramref name="filePaths"/> it becomes the
    /// current item; otherwise the first item is loaded. Clears any existing bookmarks/queue state.
    /// </summary>
    public async Task LoadCollectionAsync(IReadOnlyList<string> filePaths, string? startFilePath = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(filePaths);

        var queue = new PlaybackQueue<string>(filePaths) { RepeatMode = RepeatMode };
        if (_isShuffled)
        {
            queue.Shuffle(_queue is { Count: > 0 } ? new Random() : new Random());
        }

        if (startFilePath is not null && !queue.JumpTo(startFilePath))
        {
            queue.Start();
        }
        else if (startFilePath is null)
        {
            queue.Start();
        }

        _queue = queue;
        NotifyQueueState();

        var first = queue.Current;
        if (first is not null)
        {
            await LoadAsync(first, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Advances the queue to the next item and loads it. In <see cref="PlaybackRepeat.Off"/> mode once
    /// the end is reached it returns false without changing media; <see cref="PlaybackRepeat.All"/>
    /// wraps, and <see cref="PlaybackRepeat.One"/> reloads the current item.
    /// </summary>
    public async Task<bool> PlayNextAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_queue is null || _queue.Count == 0)
        {
            return false;
        }

        var next = _queue.MoveNext();
        NotifyQueueState();
        if (next is null)
        {
            return false;
        }

        await LoadAsync(next, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Moves back to the previous item in the queue and loads it, wrapping to the last from the first.</summary>
    public async Task<bool> PlayPreviousAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_queue is null || _queue.Count == 0)
        {
            return false;
        }

        var previous = _queue.MovePrevious();
        NotifyQueueState();
        if (previous is null)
        {
            return false;
        }

        await LoadAsync(previous, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Toggles shuffle on the current queue, keeping the current item at the front.</summary>
    public void ToggleShuffleAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_queue is null || _queue.Count == 0)
        {
            return;
        }

        if (IsShuffled)
        {
            _queue.Unshuffle();
            IsShuffled = false;
        }
        else
        {
            _queue.Shuffle(new Random());
            IsShuffled = true;
        }

        NotifyQueueState();
    }

    /// <summary>Cycles repeat mode Off → One → All → Off.</summary>
    public void CycleRepeatAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        RepeatMode = RepeatMode switch
        {
            PlaybackRepeat.Off => PlaybackRepeat.One,
            PlaybackRepeat.One => PlaybackRepeat.All,
            _ => PlaybackRepeat.Off,
        };

        if (_queue is not null)
        {
            _queue.RepeatMode = RepeatMode;
        }
    }

    private void NotifyQueueState()
    {
        Notify(nameof(IsQueueActive));
        Notify(nameof(IsShuffled));
        Notify(nameof(QueuePositionLabel));
        Notify(nameof(HasPrevious));
        Notify(nameof(HasNext));
    }

    // ── Bookmarks (PICKLE.md §21) ───────────────────────────────────────────

    /// <summary>True when the loaded file is a cataloged library item, so bookmarks can be added.</summary>
    public bool CanBookmark => _repository != null && _libraryItemId.HasValue;

    /// <summary>Bookmarks for the current cataloged item, in creation order.</summary>
    public IReadOnlyList<Bookmark> Bookmarks
    {
        get => _bookmarks;
        private set { _bookmarks = value; Notify(); }
    }

    /// <summary>Reloads the bookmark list for the current cataloged item. Best-effort, never throws.</summary>
    public async Task LoadBookmarksAsync(CancellationToken cancellationToken = default)
    {
        var id = _libraryItemId;
        if (_repository == null || !id.HasValue)
        {
            Bookmarks = [];
            return;
        }

        try
        {
            Bookmarks = await _repository.GetBookmarksAsync(id.Value, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Bookmarks = [];
        }
    }

    /// <summary>Adds a bookmark at the current playback position. Best-effort; reloads the list on success.</summary>
    public async Task<bool> AddBookmarkAtCurrentAsync(CancellationToken cancellationToken = default)
    {
        var id = _libraryItemId;
        if (_repository == null || !id.HasValue)
        {
            return false;
        }

        try
        {
            var ms = (long)Math.Max(0, _engine.Position.TotalMilliseconds);
            await _repository.AddBookmarkAsync(id.Value, ms, cancellationToken: cancellationToken).ConfigureAwait(false);
            await LoadBookmarksAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Removes a bookmark by id. Best-effort; reloads the list on success.</summary>
    public async Task RemoveBookmarkAsync(int bookmarkId, CancellationToken cancellationToken = default)
    {
        if (_repository == null) return;

        try
        {
            await _repository.RemoveBookmarkAsync(bookmarkId, cancellationToken).ConfigureAwait(false);
            await LoadBookmarksAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort: ignore failures and leave the list stale.
        }
    }

    /// <summary>Seeks the player to a bookmark's timestamp. Best-effort, never throws.</summary>
    public async Task SeekToBookmarkAsync(Bookmark bookmark)
    {
        if (bookmark == null) return;

        try
        {
            var target = TimeSpan.FromMilliseconds(Math.Max(0, bookmark.PositionMs));
            var clamped = target > _engine.Duration ? _engine.Duration : target;
            await _engine.SeekAsync(clamped).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort seek.
        }
    }

    // ── Auto-chapters (AI-06, PICKLE.md §38) ───────────────────────────────

    /// <summary>True when the loaded file is a cataloged library item and the chapters runner is available.</summary>
    public bool CanGenerateChapters => _chaptersRunner != null && _repository != null && _libraryItemId.HasValue;

    /// <summary>True while an auto-chapters generation is in flight.</summary>
    public bool IsGeneratingChapters
    {
        get => _isGeneratingChapters;
        private set
        {
            if (_isGeneratingChapters == value) return;
            _isGeneratingChapters = value;
            Notify();
            Notify(nameof(CanGenerateChapters));
        }
    }

    /// <summary>The last auto-chapters status message (result or failure), for the Chapters drawer.</summary>
    public string? ChaptersMessage
    {
        get => _chaptersMessage;
        private set { if (_chaptersMessage != value) { _chaptersMessage = value; Notify(); } }
    }

    /// <summary>Auto-generated chapters for the current cataloged item, ordered by start.</summary>
    public IReadOnlyList<AiChapterEntity> Chapters
    {
        get => _chapters;
        private set { _chapters = value; Notify(); }
    }

    /// <summary>Reloads the auto-generated chapters for the current cataloged item. Best-effort, never throws.</summary>
    public async Task LoadChaptersAsync(CancellationToken cancellationToken = default)
    {
        var id = _libraryItemId;
        if (_repository == null || !id.HasValue)
        {
            Chapters = [];
            return;
        }

        try
        {
            Chapters = await _repository.GetChaptersAsync(id.Value, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Chapters = [];
        }
    }

    /// <summary>Generates auto-chapters for the currently loaded cataloged file. Best-effort, never throws.</summary>
    public async Task<bool> GenerateChaptersAsync(CancellationToken cancellationToken = default)
    {
        if (_chaptersRunner == null || string.IsNullOrEmpty(FilePath) || _libraryItemId == null)
        {
            ChaptersMessage = "Chapters are unavailable for this file.";
            return false;
        }

        if (IsGeneratingChapters)
        {
            return false;
        }

        IsGeneratingChapters = true;
        ChaptersMessage = "Generating chapters…";
        try
        {
            var result = await _chaptersRunner.GenerateChaptersAsync(FilePath, cancellationToken).ConfigureAwait(false);
            if (result.Success)
            {
                Chapters = result.Chapters ?? [];
                ChaptersMessage = Chapters.Count > 0
                    ? $"Generated {Chapters.Count} chapters."
                    : "Chapter generation finished but produced no chapters.";
            }
            else
            {
                ChaptersMessage = result.ErrorMessage ?? "Chapter generation failed.";
            }
            return result.Success;
        }
        finally
        {
            IsGeneratingChapters = false;
        }
    }

    /// <summary>Seeks the player to a chapter's start timestamp. Best-effort, never throws.</summary>
    public async Task SeekToChapterAsync(AiChapterEntity chapter)
    {
        if (chapter == null) return;

        try
        {
            var target = TimeSpan.FromSeconds(Math.Max(0, chapter.StartSeconds));
            var clamped = target > _engine.Duration ? _engine.Duration : target;
            await _engine.SeekAsync(clamped).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort seek.
        }
    }

    /// <summary>
    /// Binds the currently loaded file to its library item (if any) and resumes from the last
    /// saved position for an unfinished item. Also computes the resume target.
    /// </summary>
    private async Task ResolveLibrarySessionAsync(CancellationToken cancellationToken)
    {
        _libraryItemId = null;
        _libraryItem = null;
        _lastRecordedPosition = TimeSpan.MinValue;
        _lastPeriodicRecordUtc = DateTime.MinValue;
        _thumbnailCaptureStarted = false;
        Chapters = [];
        ChaptersMessage = null;

        if (_repository is null || string.IsNullOrEmpty(FilePath))
        {
            return;
        }

        LibraryMediaItem? item;
        try
        {
            item = await _repository.FindByPathAsync(FilePath, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Library lookup failed: {ex}");
            return;
        }

        if (item is null)
        {
            return;
        }

        _libraryItemId = item.Id;
        _libraryItem = item;

        // Resume only unfinished (partially watched) media, never a finished one.
        if (item.WatchedPercentage >= 100.0 || item.LastPosition <= TimeSpan.Zero)
        {
            return;
        }

        try
        {
            var target = item.LastPosition;
            var duration = _engine.Duration;
            if (duration > TimeSpan.Zero && target > duration)
            {
                target = duration;
            }
            await _engine.SeekAsync(target, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Resume is best-effort — a seek failure must not block playback.
            System.Diagnostics.Debug.WriteLine($"Resume seek failed: {ex}");
        }
    }

    /// <summary>Toggles between Play and Pause based on current state.</summary>
    public async Task PlayPauseAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_engine.State == PlaybackState.Playing)
            await _engine.PauseAsync().ConfigureAwait(false);
        else
        {
            _mediaEnded = false;
            await _engine.PlayAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Stops playback and resets position to zero.</summary>
    public async Task StopAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var finalPosition = _engine.Position;
        await _engine.StopAsync().ConfigureAwait(false);
        await RecordSessionAsync(finalPosition).ConfigureAwait(false);
    }

    /// <summary>
    /// True when the loaded session actually has a video picture that can be captured as a snapshot:
    /// a video track must exist and we must know where the media is (local path or stream URI).
    /// </summary>
    public bool CanCaptureSnapshot => VideoTracks.Count > 0 && !string.IsNullOrWhiteSpace(FilePath);

    /// <summary>
    /// Captures the current video frame as a PNG at <paramref name="outputPath"/> (Phase 7). Returns
    /// false — without throwing — when there is nothing video-like loaded, the path is empty, the
    /// output directory cannot be created, or the engine's native snapshot fails (e.g. no rendered
    /// frame yet). The UI shows a response dialog; playback is never disturbed.
    /// </summary>
    public async Task<bool> SaveSnapshotAsync(string outputPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!CanCaptureSnapshot || string.IsNullOrWhiteSpace(outputPath))
        {
            return false;
        }

        try
        {
            var directory = Path.GetDirectoryName(outputPath);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                if (string.IsNullOrEmpty(directory)) return false;
                Directory.CreateDirectory(directory);
            }

            return await _engine.TakeSnapshotAsync(outputPath).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Snapshot capture failed: {ex}");
            return false;
        }
    }

    // ── Mark In / Out clip editing (Phase 7, PICKLE.md §1102) ─────────────────

    /// <summary>Start of the exportable clip region, or null when not marked.</summary>
    public TimeSpan? MarkIn => _markIn;

    /// <summary>End of the exportable clip region, or null when not marked.</summary>
    public TimeSpan? MarkOut => _markOut;

    /// <summary>True when a valid, ordered In&lt;Out range is marked on non-empty media.</summary>
    public bool IsMarked => _markIn is { } i && _markOut is { } o && o > i && _engine.Duration > TimeSpan.Zero;

    /// <summary>Horizontal position (0–100) of the In mark for the timeline overlay.</summary>
    public double MarkInPercent => MarkPercent(_markIn);

    /// <summary>Horizontal position (0–100) of the Out mark for the timeline overlay.</summary>
    public double MarkOutPercent => MarkPercent(_markOut);

    /// <summary>True when a valid clip range is marked on a local file and an exporter exists.</summary>
    public bool CanExportClip => IsMarked && _clipExporter != null &&
                                 !string.IsNullOrWhiteSpace(FilePath) &&
                                 !MediaFile.FromPathOrUri(FilePath).IsRemoteUri;

    /// <summary>Marks the In point at the current playhead. A later In clears an earlier Out.</summary>
    public void SetMarkIn()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_engine.Duration <= TimeSpan.Zero || !_engine.IsSeekable) return;
        _markIn = _engine.Position;
        if (_markOut is { } o && o <= _markIn) _markOut = null;
        NotifyMarkState();
    }

    /// <summary>Marks the Out point at the current playhead. An earlier Out clears a later In.</summary>
    public void SetMarkOut()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_engine.Duration <= TimeSpan.Zero || !_engine.IsSeekable) return;
        _markOut = _engine.Position;
        if (_markIn is { } i && i >= _markOut) _markIn = null;
        NotifyMarkState();
    }

    /// <summary>Clears both marks for the current file.</summary>
    public void ClearMarks()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_markIn is null && _markOut is null) return;
        _markIn = null;
        _markOut = null;
        NotifyMarkState();
    }

    /// <summary>
    /// Exports the marked In→Out range of the loaded local file to <paramref name="outputPath"/>.
    /// Best-effort: never throws — an unavailable exporter, invalid marks, an empty path or a failed
    /// ffmpeg run returns a <see cref="ClipExportResult.Fail"/> result.
    /// </summary>
    public async Task<ClipExportResult> ExportClipAsync(string outputPath, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_clipExporter is null)
        {
            return ClipExportResult.Fail("Clip export is not available on this install.");
        }
        if (!CanExportClip || _markIn is not { } inPoint || _markOut is not { } outPoint || outPoint <= inPoint)
        {
            return ClipExportResult.Fail("Mark a valid In and Out point before exporting.");
        }
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return ClipExportResult.Fail("Choose an export path first.");
        }

        var localPath = FilePath;
        if (string.IsNullOrWhiteSpace(localPath))
        {
            return ClipExportResult.Fail("No media file is loaded to export.");
        }

        try
        {
            return await _clipExporter.ExportClipAsync(localPath, outputPath, inPoint, outPoint - inPoint, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Clip export failed: {ex}");
            return ClipExportResult.Fail(ex.Message);
        }
    }

    // ── Stereo balance (ADR-0011, docs/design/stereo-balance-av-sync.md) ──────

    /// <summary>Current balance value, -100 (full left) .. +100 (full right), 0 = centered.</summary>
    public int StereoBalanceValue => _stereoBalance.Value;

    /// <summary>True when the secondary balance-audio path is currently running (engine audio is muted).</summary>
    public bool IsStereoBalanceActive => _stereoBalanceSession?.IsActive ?? false;

    /// <summary>True when balance can be adjusted: a session exists and a local file is loaded.</summary>
    public bool CanUseStereoBalance => _stereoBalanceSession != null &&
                                        !string.IsNullOrWhiteSpace(FilePath) &&
                                        !MediaFile.FromPathOrUri(FilePath).IsRemoteUri;

    /// <summary>
    /// Sets the stereo balance. A centered value stops the secondary audio path and restores normal
    /// (unmuted) engine audio; any other value starts/updates the secondary path and mutes the
    /// engine's own output while it runs (only libVLC's audio would otherwise double up with the
    /// balance-filtered stream). Best-effort: if the secondary path fails to start, the engine is
    /// left unmuted and the balance value is not changed, so the user never loses audio entirely.
    /// </summary>
    public async Task SetStereoBalanceAsync(int value, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_stereoBalanceSession is null || !CanUseStereoBalance)
        {
            return;
        }

        var balance = new StereoBalance(StereoBalance.Clamp(value));

        try
        {
            if (balance.IsCentered)
            {
                if (_stereoBalanceSession.IsActive)
                {
                    await _stereoBalanceSession.StopAsync().ConfigureAwait(false);
                    await _engine.SetMutedAsync(false, cancellationToken).ConfigureAwait(false);
                }
                _stereoBalance = balance;
            }
            else if (_stereoBalanceSession.IsActive)
            {
                var updated = await _stereoBalanceSession.UpdateBalanceAsync(balance, () => _engine.Position, cancellationToken).ConfigureAwait(false);
                if (updated)
                {
                    _stereoBalance = balance;
                }
            }
            else if (!string.IsNullOrWhiteSpace(FilePath))
            {
                var started = await _stereoBalanceSession.StartAsync(FilePath, balance, () => _engine.Position, cancellationToken).ConfigureAwait(false);
                if (started)
                {
                    await _engine.SetMutedAsync(true, cancellationToken).ConfigureAwait(false);
                    _stereoBalance = balance;
                }
            }
        }
        catch (Exception ex)
        {
            // Best-effort — never let a balance-audio failure take playback down.
            System.Diagnostics.Debug.WriteLine($"Stereo balance update failed: {ex}");
        }

        Notify(nameof(StereoBalanceValue));
        Notify(nameof(IsStereoBalanceActive));
    }

    /// <summary>Stops any active secondary balance-audio path and restores normal engine audio, best-effort.</summary>
    private async Task DisableStereoBalanceAsync()
    {
        if (_stereoBalanceSession is null || !_stereoBalanceSession.IsActive)
        {
            _stereoBalance = StereoBalance.Center;
            return;
        }

        try
        {
            await _stereoBalanceSession.StopAsync().ConfigureAwait(false);
            await _engine.SetMutedAsync(false).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Stereo balance teardown failed: {ex}");
        }
        finally
        {
            _stereoBalance = StereoBalance.Center;
        }
    }

    private double MarkPercent(TimeSpan? mark)
    {
        if (mark is not { } t || _engine.Duration <= TimeSpan.Zero)
        {
            return 0;
        }
        return Math.Clamp(t.TotalMilliseconds / _engine.Duration.TotalMilliseconds * 100.0, 0.0, 100.0);
    }

    private void NotifyMarkState()
    {
        Notify(nameof(MarkIn));
        Notify(nameof(MarkOut));
        Notify(nameof(IsMarked));
        Notify(nameof(MarkInPercent));
        Notify(nameof(MarkOutPercent));
        Notify(nameof(CanExportClip));
    }

    // ── Phase 7 (PS-703): Media conversion ───────────────────────────────────

    /// <summary>True when the loaded file is a local media file and a converter is installed.</summary>
    public bool CanConvert => _mediaConverter != null &&
                              !string.IsNullOrWhiteSpace(FilePath) &&
                              !MediaFile.FromPathOrUri(FilePath).IsRemoteUri;

    /// <summary>True while a conversion is running (disables the Convert entry point).</summary>
    public bool IsConverting
    {
        get => _isConverting;
        private set
        {
            if (_isConverting == value)
            {
                return;
            }

            _isConverting = value;
            Notify(nameof(IsConverting));
            Notify(nameof(CanConvert));
        }
    }

    /// <summary>
    /// Converts the loaded local file to <paramref name="outputPath"/> using the codec/mapping of
    /// <paramref name="preset"/> (PICKLE.md §29). Best-effort: never throws — an unavailable
    /// converter, a remote stream, an empty path or a failed ffmpeg run returns a
    /// <see cref="ConversionResult.Fail"/> result.
    /// </summary>
    public async Task<ConversionResult> ConvertAsync(ConversionPreset preset, string outputPath, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_mediaConverter is null)
        {
            return ConversionResult.Fail("Conversion is not available on this install.");
        }
        if (!CanConvert)
        {
            return ConversionResult.Fail("Only local media files can be converted.");
        }
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return ConversionResult.Fail("Choose an output path first.");
        }

        var localPath = FilePath;
        if (string.IsNullOrWhiteSpace(localPath))
        {
            return ConversionResult.Fail("No media file is loaded to convert.");
        }

        if (IsConverting)
        {
            return ConversionResult.Fail("A conversion is already in progress.");
        }

        IsConverting = true;
        try
        {
            return await _mediaConverter.ConvertAsync(localPath, outputPath, preset, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Conversion failed: {ex}");
            return ConversionResult.Fail(ex.Message);
        }
        finally
        {
            IsConverting = false;
        }
    }

    // ── Phase 7 (PS-704): Timeline hover thumbnails (PICKLE.md §20) ─────────

    /// <summary>
    /// The generated timeline filmstrip for the currently loaded local file, or null while
    /// unavailable (streams, files without video, provider missing, generation in flight/failed).
    /// </summary>
    public ThumbnailStripResult? ThumbnailStrip
    {
        get => _thumbnailStrip;
        private set { if (_thumbnailStrip != value) { _thumbnailStrip = value; Notify(); } }
    }

    /// <summary>
    /// Grid index (0-based row-major position) into <see cref="ThumbnailStrip"/> for a timeline
    /// fraction 0–100, clamped to the sheet bounds. Returns -1 when no strip is available.
    /// </summary>
    public int GetThumbnailCellIndex(double percent)
    {
        if (_thumbnailStrip is not { Success: true, Columns: > 0, Rows: > 0 } strip)
        {
            return -1;
        }

        var cells = strip.Columns * strip.Rows;
        var index = (int)(Math.Clamp(percent, 0.0, 100.0) / 100.0 * cells);
        return Math.Clamp(index, 0, cells - 1);
    }

    /// <summary>
    /// Prepares the hover-thumbnail filmstrip for the loaded local file. Best-effort and
    /// fire-and-forget from the call site: a missing provider, remote stream, unknown duration or
    /// ffmpeg failure simply leaves <see cref="ThumbnailStrip"/> null and never affects playback.
    /// </summary>
    public async Task PrepareThumbnailStripAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_thumbnailProvider is null) return;

        var localPath = FilePath;
        if (string.IsNullOrWhiteSpace(localPath) || MediaFile.FromPathOrUri(localPath).IsRemoteUri)
        {
            return;
        }

        if (VideoTracks.Count == 0)
        {
            // Audio has no picture to preview; skip generation entirely.
            return;
        }

        var duration = _engine.Duration;
        if (duration <= TimeSpan.Zero || !_engine.IsSeekable)
        {
            return;
        }

        try
        {
            // More cells for longer files, still bounded by the provider's own MaximumCells.
            var frameCount = Math.Clamp((int)(duration.TotalMinutes) + 8, 16, 96);
            ThumbnailStrip = await _thumbnailProvider
                .GenerateStripAsync(localPath, _thumbnailStripCacheDir ?? DefaultThumbnailStripDirectory(), duration, frameCount, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Thumbnail strip generation failed: {ex}");
        }
    }

    private void ClearThumbnailStrip()
    {
        if (_thumbnailStrip is not null)
        {
            ThumbnailStrip = null;
        }
    }

    private static string DefaultThumbnailStripDirectory() =>
        Path.Combine(DiagnosticReportLocations.DefaultDataDirectory(), "thumbstrips");

    // ── Phase 7 (PS-705): Timeline waveform (PICKLE.md §20) ─────────────────

    /// <summary>
    /// The generated timeline peak-level waveform strip for the loaded local file, or null while
    /// unavailable (streams, files without audio, provider missing, generation in flight/failed).
    /// </summary>
    public WaveformResult? Waveform
    {
        get => _waveform;
        private set { if (_waveform != value) { _waveform = value; Notify(); } }
    }

    /// <summary>
    /// Prepares the timeline waveform for the loaded local file. Best-effort and fire-and-forget
    /// from the call site: a missing provider, remote stream, unknown duration, missing audio track
    /// or ffmpeg failure simply leaves <see cref="Waveform"/> null and never affects playback.
    /// </summary>
    public async Task PrepareWaveformAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_waveformProvider is null) return;

        var localPath = FilePath;
        if (string.IsNullOrWhiteSpace(localPath) || MediaFile.FromPathOrUri(localPath).IsRemoteUri)
        {
            return;
        }

        if (AudioTracks.Count == 0)
        {
            // No audio track to render a waveform from; skip generation entirely.
            return;
        }

        if (_engine.Duration <= TimeSpan.Zero || !_engine.IsSeekable)
        {
            return;
        }

        try
        {
            Waveform = await _waveformProvider
                .GenerateAsync(localPath, _waveformCacheDir ?? DefaultWaveformDirectory(), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Waveform generation failed: {ex}");
        }
    }

    private void ClearWaveform()
    {
        if (_waveform is not null)
        {
            Waveform = null;
        }
    }

    private static string DefaultWaveformDirectory() =>
        Path.Combine(DiagnosticReportLocations.DefaultDataDirectory(), "waveforms");

    /// <summary>Seeks backward by 10 seconds, clamped to zero.</summary>
    public async Task SkipBackAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_engine.IsSeekable) return;
        var target = _engine.Position - SkipAmount;
        if (target < TimeSpan.Zero) target = TimeSpan.Zero;
        await SeekTargetAsync(target).ConfigureAwait(false);
    }

    /// <summary>Seeks forward by 10 seconds, clamped to duration.</summary>
    public async Task SkipForwardAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_engine.IsSeekable) return;
        var target = _engine.Position + SkipAmount;
        if (target > _engine.Duration) target = _engine.Duration;
        await SeekTargetAsync(target).ConfigureAwait(false);
    }

    /// <summary>Seeks relative to current position by an arbitrary offset (clamped between 0 and duration).</summary>
    public async Task SeekRelativeAsync(TimeSpan offset)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_engine.Duration <= TimeSpan.Zero || !_engine.IsSeekable) return;

        var target = _engine.Position + offset;
        if (target < TimeSpan.Zero) target = TimeSpan.Zero;
        if (target > _engine.Duration) target = _engine.Duration;

        await SeekTargetAsync(target).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies a seek to <paramref name="target"/>. If the media had finished (EndReached) this
    /// reloads + restarts playback FIRST — then waits for the native Playing event and seeks via the
    /// reliable playing-path Time-set. A stopped/ended native player silently drops a fractional
    /// Position and a plain Play() does not reliably restart a fully-consumed media in libVLC, so
    /// without this a post-end scrub leaves the video frozen on the "completed" frame regardless of
    /// seek-bar movement. Reload re-applies audio-enhancements/subtitle-style and renders to the
    /// already-attached surface (the normal load path — no native renderer rebuild and no
    /// event-callback Time-set, so outside the recorded AccessViolationException risk class). A
    /// live/paused media is a plain seek (no reload, no auto-play).
    /// </summary>
    private async Task SeekTargetAsync(TimeSpan target)
    {
        if (!_mediaEnded)
        {
            await _engine.SeekAsync(target).ConfigureAwait(false);
            NotifyStereoBalanceSeeked(target);
            return;
        }

        _mediaEnded = false;

        // Reload the same media to get a fresh, playable native player (Play() on an ended session
        // is unreliable). Deliberately the engine's LoadAsync, not the VM's (avoids re-running the
        // library resume-session wiring).
        if (!string.IsNullOrEmpty(FilePath))
        {
            await _engine.LoadAsync(MediaFile.FromPathOrUri(FilePath)).ConfigureAwait(false);
        }

        await _engine.PlayAsync().ConfigureAwait(false);
        for (var i = 0; i < 100 && _engine.State != PlaybackState.Playing; i++)
            await Task.Delay(20).ConfigureAwait(false);
        await _engine.SeekAsync(target).ConfigureAwait(false);
        NotifyStereoBalanceSeeked(target);
    }

    /// <summary>
    /// Fire-and-forget, best-effort notice to the active balance-audio session that the engine
    /// transport jumped (design note item 4) — never awaited from the seek path so scrubbing the
    /// timeline doesn't stall on the secondary session's ffmpeg-restart latency.
    /// </summary>
    private void NotifyStereoBalanceSeeked(TimeSpan target)
    {
        if (_stereoBalanceSession is not { IsActive: true } session)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await session.NotifyEngineSeekedAsync(target).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Stereo balance seek notify failed: {ex}");
            }
        });
    }


    /// <summary>Calculates the timestamp for a given fraction 0..100 across the total duration.</summary>
    public TimeSpan GetTimestampForPercent(double percent)
    {
        if (_engine.Duration <= TimeSpan.Zero) return TimeSpan.Zero;
        var clampedPercent = Math.Clamp(percent, 0.0, 100.0);
        return TimeSpan.FromSeconds(clampedPercent / 100.0 * _engine.Duration.TotalSeconds);
    }

    /// <summary>Returns formatted preview text ("0:15 / 1:30") for a given fraction 0..100.</summary>
    public string GetHoverPreviewText(double percent)
    {
        var target = GetTimestampForPercent(percent);
        return $"{FormatTime(target)} / {DurationText}";
    }

    /// <summary>Cycles playback rate through standard increments: 0.5× → 1.0× → 1.25× → 1.5× → 2.0× → 0.5×</summary>
    public async Task CycleRateAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        double[] speeds = [0.5, 1.0, 1.25, 1.5, 2.0];
        var current = _engine.Rate;
        var next = speeds.FirstOrDefault(s => s > current + 0.01);
        if (next == 0.0) next = speeds[0];

        await SetRateAsync(next).ConfigureAwait(false);
    }

    /// <summary>Sets explicit playback rate (e.g. 0.25 to 4.0).</summary>
    public async Task SetRateAsync(double rate)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var clamped = Math.Clamp(rate, 0.25, 4.0);
        await _engine.SetRateAsync(clamped).ConfigureAwait(false);
        Notify(nameof(Rate));
        Notify(nameof(RateText));
    }

    /// <summary>Selects active audio track by ID. Learns the chosen track's language as a preference.</summary>
    public async Task SelectAudioTrackAsync(int? trackId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _engine.SelectAudioTrackAsync(trackId).ConfigureAwait(false);
        SelectedAudioTrackId = trackId;

        _autoSelectAudioDone = true;
        if (trackId is null || _preferences is null)
        {
            return;
        }

        // Remember the language of the user-picked track so future loads auto-select it
        // (most recent/strongest preference goes to the front).
        var language = AudioTracks.FirstOrDefault(t => t.Id == trackId.Value)?.Language;
        if (string.IsNullOrWhiteSpace(language))
        {
            return;
        }

        try
        {
            var current = new List<string>(_preferences.GetPreferredAudioLanguages());
            current.RemoveAll(l => string.Equals(l, language, StringComparison.OrdinalIgnoreCase));
            current.Insert(0, language);
            _preferences.SetPreferredAudioLanguages(current);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Preference persist failed: {ex}");
        }
    }

    /// <summary>Selects active subtitle track by ID (null = off).</summary>
    public async Task SelectSubtitleTrackAsync(int? trackId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _engine.SelectSubtitleTrackAsync(trackId).ConfigureAwait(false);
        SelectedSubtitleTrackId = trackId;
    }

    /// <summary>
    /// Loads an external subtitle file (SRT, ASS, SSA, VTT, SUB, etc.) and activates it.
    /// </summary>
    public async Task<int> LoadExternalSubtitleAsync(string subtitlePath, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(subtitlePath);

        var trackId = await _engine.LoadExternalSubtitleAsync(subtitlePath, cancellationToken).ConfigureAwait(false);
        SelectedSubtitleTrackId = trackId;
        await TryParseSubtitleDocumentAsync(subtitlePath, cancellationToken).ConfigureAwait(false);
        return trackId;
    }

    /// <summary>
    /// Parses <paramref name="subtitlePath"/> into <see cref="LoadedSubtitleDocument"/> for cue
    /// search/list/export (PICKLE.md §17), when its format has a parser (currently SRT/WebVTT —
    /// ASS/SSA/SUB still render via the engine directly but aren't parsed for cue browsing).
    /// Best-effort: parsing failures never interrupt subtitle playback, which has already
    /// succeeded via the engine by the time this runs.
    /// </summary>
    private async Task TryParseSubtitleDocumentAsync(string subtitlePath, CancellationToken cancellationToken)
    {
        try
        {
            var parser = SubtitleParserFactory.ForFile(subtitlePath);
            if (parser is null) return;

            var content = await File.ReadAllTextAsync(subtitlePath, cancellationToken).ConfigureAwait(false);
            LoadedSubtitleDocument = parser.Parse(content);
        }
        catch (Exception)
        {
            // Cue browsing is a bonus feature; a parse failure must not affect playback.
        }
    }

    /// <summary>
    /// Scans the directory of the currently loaded media file for subtitle files matching its
    /// name (via <see cref="SubtitleFileMatcher"/>, PICKLE.md §17 "automatic filename matching")
    /// and loads the best-ranked match. Returns the track ID or -1 if none found.
    /// </summary>
    public async Task<int> TryAutoLoadSubtitleAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrEmpty(FilePath)) return -1;

        try
        {
            var match = SubtitleFileMatcher.FindMatches(FilePath).FirstOrDefault();
            if (match is not null)
            {
                return await LoadExternalSubtitleAsync(match.FilePath, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // Auto-load is best-effort; don't interrupt playback on any error.
        }

        return -1;
    }

    /// <summary>
    /// Searches the currently loaded subtitle's cues for <paramref name="query"/> (case-insensitive
    /// substring match). Returns an empty list when no subtitle document is loaded.
    /// </summary>
    public IReadOnlyList<SubtitleCue> SearchSubtitleCues(string query) =>
        _loadedSubtitleDocument?.Search(query).ToList() ?? [];

    /// <summary>Seeks playback to a cue's start timestamp.</summary>
    public async Task SeekToCueAsync(SubtitleCue cue)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(cue);
        await _engine.SeekAsync(cue.Start).ConfigureAwait(false);
    }

    /// <summary>
    /// Exports the currently loaded subtitle's cues as SRT to <paramref name="outputPath"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">No parseable subtitle is currently loaded.</exception>
    public async Task ExportLoadedSubtitleAsync(string outputPath, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (_loadedSubtitleDocument is null)
        {
            throw new InvalidOperationException("No subtitle document is loaded to export.");
        }

        await SubtitleExporter.WriteSrtAsync(_loadedSubtitleDocument, outputPath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Sets subtitle delay offset directly.</summary>
    public async Task SetSubtitleDelayAsync(TimeSpan delay)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _engine.SetSubtitleDelayAsync(delay).ConfigureAwait(false);
        Notify(nameof(SubtitleDelay));
        Notify(nameof(SubtitleDelayMilliseconds));
        Notify(nameof(SubtitleDelayText));
    }

    /// <summary>Adjusts subtitle delay offset by a relative delta (e.g. +50ms or -50ms).</summary>
    public async Task AdjustSubtitleDelayAsync(TimeSpan delta)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var current = _engine.SubtitleDelay;
        var next = current + delta;
        await SetSubtitleDelayAsync(next).ConfigureAwait(false);
    }

    /// <summary>Resets subtitle delay offset to 0 ms.</summary>
    public async Task ResetSubtitleDelayAsync()
    {
        await SetSubtitleDelayAsync(TimeSpan.Zero).ConfigureAwait(false);
    }

    /// <summary>Sets audio delay (A/V sync) offset directly.</summary>
    public async Task SetAudioDelayAsync(TimeSpan delay)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _engine.SetAudioDelayAsync(delay).ConfigureAwait(false);
        Notify(nameof(AudioDelay));
        Notify(nameof(AudioDelayMilliseconds));
        Notify(nameof(AudioDelayText));
    }

    /// <summary>Adjusts audio delay (A/V sync) offset by a relative delta (e.g. +50ms or -50ms).</summary>
    public async Task AdjustAudioDelayAsync(TimeSpan delta)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var current = _engine.AudioDelay;
        var next = current + delta;
        await SetAudioDelayAsync(next).ConfigureAwait(false);
    }

    /// <summary>Resets audio delay (A/V sync) offset to 0 ms.</summary>
    public async Task ResetAudioDelayAsync()
    {
        await SetAudioDelayAsync(TimeSpan.Zero).ConfigureAwait(false);
    }

    /// <summary>Enables or disables the 10-band equalizer.</summary>
    public async Task SetEqualizerEnabledAsync(bool enabled)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _engine.SetEqualizerEnabledAsync(enabled).ConfigureAwait(false);
    }

    /// <summary>Applies a named equalizer preset (e.g. "Flat", "Rock", "Pop").</summary>
    public async Task SetEqualizerPresetAsync(string presetName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _engine.SetEqualizerPresetAsync(presetName).ConfigureAwait(false);
    }

    /// <summary>Sets a single equalizer band's gain in dB (band 0–9).</summary>
    public async Task SetEqualizerBandGainAsync(uint bandIndex, float gainDb)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _engine.SetEqualizerBandGainAsync(bandIndex, gainDb).ConfigureAwait(false);
    }

    /// <summary>Sets the equalizer preamp gain in dB.</summary>
    public async Task SetEqualizerPreampAsync(float preampDb)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _engine.SetEqualizerPreampAsync(preampDb).ConfigureAwait(false);
    }

    /// <summary>Resets the equalizer to flat/disabled.</summary>
    public async Task ResetEqualizerAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _engine.ResetEqualizerAsync().ConfigureAwait(false);
    }

    /// <summary>Selects an explicit audio output device (takes effect after stop/restart).</summary>
    public async Task SetAudioOutputDeviceAsync(string deviceId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _engine.SetAudioOutputDeviceAsync(deviceId).ConfigureAwait(false);
        Notify(nameof(ActiveAudioOutputDeviceId));
    }

    /// <summary>Sets the stereo/mono downmix channel mode.</summary>
    public async Task SetAudioChannelAsync(AudioChannelMode mode)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _engine.SetAudioChannelAsync(mode).ConfigureAwait(false);
        Notify(nameof(AudioChannel));
    }

    /// <summary>Toggles loudness normalization (applies starting with the next media load).</summary>
    public async Task SetLoudnessNormalizationEnabledAsync(bool enabled)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var next = _engine.AudioEnhancements with { LoudnessNormalizationEnabled = enabled };
        await _engine.SetAudioEnhancementsAsync(next).ConfigureAwait(false);
        Notify(nameof(IsLoudnessNormalizationEnabled));
    }

    /// <summary>Toggles night mode / dynamic range compression (applies starting with the next media load).</summary>
    public async Task SetNightModeEnabledAsync(bool enabled)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var next = _engine.AudioEnhancements with { NightModeEnabled = enabled };
        await _engine.SetAudioEnhancementsAsync(next).ConfigureAwait(false);
        Notify(nameof(IsNightModeEnabled));
    }

    /// <summary>Sets subtitle text scale as a percent, 25–500 (applies next load).</summary>
    public async Task SetSubtitleTextScalePercentAsync(int percent)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var style = _engine.SubtitleStyle with { TextScalePercent = percent };
        await _engine.SetSubtitleStyleAsync(style).ConfigureAwait(false);
        Notify(nameof(SubtitleTextScalePercent));
        Notify(nameof(SubtitleStyle));
    }

    /// <summary>Sets the subtitle font family, or null for the system default (applies next load).</summary>
    public async Task SetSubtitleFontAsync(string? fontFamily)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var style = _engine.SubtitleStyle with { FontFamily = string.IsNullOrWhiteSpace(fontFamily) ? null : fontFamily };
        await _engine.SetSubtitleStyleAsync(style).ConfigureAwait(false);
        Notify(nameof(SubtitleFontFamily));
        Notify(nameof(SubtitleStyle));
    }

    /// <summary>Sets the subtitle text color as 0xRRGGBB, or null for the default (applies next load).</summary>
    public async Task SetSubtitleColorAsync(int? argb)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var style = _engine.SubtitleStyle with { TextColorArgb = argb };
        await _engine.SetSubtitleStyleAsync(style).ConfigureAwait(false);
        Notify(nameof(SubtitleTextColorArgb));
        Notify(nameof(SubtitleStyle));
    }

    /// <summary>Sets the subtitle outline color as 0xRRGGBB, or null for the default (applies next load).</summary>
    public async Task SetSubtitleOutlineColorAsync(int? argb)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var style = _engine.SubtitleStyle with { OutlineColorArgb = argb };
        await _engine.SetSubtitleStyleAsync(style).ConfigureAwait(false);
        Notify(nameof(SubtitleOutlineColorArgb));
        Notify(nameof(SubtitleStyle));
    }

    /// <summary>Sets the subtitle outline thickness (applies next load).</summary>
    public async Task SetSubtitleOutlineAsync(SubtitleOutlineStyle outline)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var style = _engine.SubtitleStyle with { Outline = outline };
        await _engine.SetSubtitleStyleAsync(style).ConfigureAwait(false);
        Notify(nameof(SubtitleOutline));
        Notify(nameof(SubtitleStyle));
    }

    /// <summary>Sets the subtitle vertical position as a bottom margin in pixels (applies next load).</summary>
    public async Task SetSubtitleVerticalMarginAsync(int marginPx)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var style = _engine.SubtitleStyle with { VerticalMarginPx = marginPx };
        await _engine.SetSubtitleStyleAsync(style).ConfigureAwait(false);
        Notify(nameof(SubtitleVerticalMarginPx));
        Notify(nameof(SubtitleStyle));
    }

    /// <summary>Opens a specific drawer.</summary>
    public void OpenDrawer(PlayerDrawerKind kind)
    {
        ActiveDrawer = kind;
    }

    /// <summary>Closes any active drawer.</summary>
    public void CloseDrawer()
    {
        ActiveDrawer = PlayerDrawerKind.None;
    }

    /// <summary>Toggles a drawer open or closed.</summary>
    public void ToggleDrawer(PlayerDrawerKind kind)
    {
        ActiveDrawer = ActiveDrawer == kind ? PlayerDrawerKind.None : kind;
    }

    /// <summary>Call when user begins dragging the timeline slider to pause external position updates.</summary>
    public void BeginSeek() => _isSeeking = true;

    /// <summary>Call when user finishes dragging the timeline slider.</summary>
    public void EndSeek() => _isSeeking = false;

    /// <summary>Seeks to a fraction position 0–100 from the timeline slider.</summary>
    public async Task SeekToPercentAsync(double percent)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_engine.Duration <= TimeSpan.Zero || !_engine.IsSeekable) return;
        var target = TimeSpan.FromSeconds(percent / 100.0 * _engine.Duration.TotalSeconds);
        await SeekTargetAsync(target).ConfigureAwait(false);
    }

    /// <summary>
    /// Best-effort seek to an absolute position, clamping to the loaded duration. Used by
    /// transcript/timestamp navigation (AI-05) after a fresh load so the clicked moment plays.
    /// Never throws — an unseekable/empty player or a failed seek is a silent no-op.
    /// </summary>
    public async Task SeekToPositionAsync(TimeSpan position)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (position < TimeSpan.Zero || !_engine.IsSeekable) return;

        var target = position;
        var duration = _engine.Duration;
        if (duration > TimeSpan.Zero && target > duration)
        {
            target = duration;
        }

        try
        {
            await _engine.SeekAsync(target).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort seek (mirrors the library resume-seek failure handling).
        }
    }

    /// <summary>Sets volume 0–100.</summary>
    public async Task SetVolumeAsync(int volume)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _engine.SetVolumeAsync(volume).ConfigureAwait(false);
        Notify(nameof(Volume));
    }

    /// <summary>Toggles mute.</summary>
    public async Task ToggleMuteAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var muted = !_engine.IsMuted;
        await _engine.SetMutedAsync(muted).ConfigureAwait(false);
        IsMuted = muted;
    }

    // ── Engine event handlers ───────────────────────────────────────────────

    private void OnStateChanged(object? sender, PlaybackStateChangedEventArgs e)
    {
        IsPlaying = e.Current == PlaybackState.Playing;
        if (e.Current != PlaybackState.Error)
        {
            LastError = null;
        }
        Notify(nameof(StateLabel));
        Notify(nameof(DurationText));
    }

    private void OnPositionChanged(object? sender, PlaybackPositionChangedEventArgs e)
    {
        if (!_isSeeking)
        {
            Notify(nameof(PositionPercent));
            Notify(nameof(PositionText));
        }

        // Once we have a rendered frame (position ≥ 1s) and the item still lacks a cached cover
        // art still, capture one and persist its path. Best-effort and one-shot per load.
        if (!_thumbnailCaptureStarted
            && _libraryItem is { CoverArtPath: null }
            && _repository is not null
            && _isPlaying
            && e.Position >= TimeSpan.FromSeconds(1))
        {
            _thumbnailCaptureStarted = true;
            _ = CaptureThumbnailForLibraryItemAsync();
        }

        // Periodically persist progress so Continue Watching stays fresh even if playback is
        // interrupted (crash / force close). Throttled to avoid a DB write every position tick.
        if (_libraryItemId is null || _repository is null || !_isPlaying)
        {
            return;
        }
        var now = DateTime.UtcNow;
        if (now - _lastPeriodicRecordUtc < TimeSpan.FromSeconds(10))
        {
            return;
        }
        _lastPeriodicRecordUtc = now;
        _ = RecordSessionAsync();
    }

    private void OnEngineEndReached(object? sender, PlaybackEndedEventArgs e)
    {
        // Media finished: persist the final position (WatchedPercentage → 100) so it leaves
        // Continue Watching. Best-effort, fire-and-forget.
        _ = RecordSessionAsync(_engine.Duration);

        // Remember that the media ended so a later scrub on the timeline restarts playback from the
        // sought position (see SeekToPercentAsync) instead of silently doing nothing on the frozen
        // end frame. Reset by LoadAsync / a manual Play.
        _mediaEnded = true;

        // Auto-advance through an active queue (PICKLE.md §25 "play next"): move to the next item.
        // For RepeatMode.One the queue returns the same item (replaying the file) — a continuous loop.
        if (_queue is { Count: > 0 } && !_disposed)
        {
            _ = PlayNextAsync();
        }
    }

    private void OnTracksChanged(object? sender, PlaybackTracksChangedEventArgs e)
    {
        Notify(nameof(Metadata));
        Notify(nameof(AudioTracks));
        Notify(nameof(SubtitleTracks));
        Notify(nameof(VideoTracks));

        _ = AutoSelectPreferredAudioTrackAsync();
    }

    /// <summary>
    /// Auto-selects the most preferred audio track once per load, when a preference exists and the
    /// user hasn't already chosen a track. Best-effort — selection failure is swallowed.
    /// </summary>
    private async Task AutoSelectPreferredAudioTrackAsync()
    {
        if (_autoSelectAudioDone || _selectedAudioTrackId is not null || _preferences is null)
        {
            return;
        }

        var preferred = _preferences.GetPreferredAudioLanguages();
        var targetId = AudioTrackSelector.PickPreferred(AudioTracks, preferred);
        if (targetId is null)
        {
            _autoSelectAudioDone = true;
            return;
        }

        _autoSelectAudioDone = true;
        try
        {
            await _engine.SelectAudioTrackAsync(targetId.Value).ConfigureAwait(false);
            SelectedAudioTrackId = targetId;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Preferred audio-track auto-selection failed: {ex}");
        }
    }

    private void OnErrorOccurred(object? sender, PlaybackErrorEventArgs e)
    {
        LastError = e.Message;
        Notify(nameof(StateLabel));
    }

    private void OnEqualizerChanged(object? sender, PlaybackEqualizerChangedEventArgs e)
    {
        Notify(nameof(IsEqualizerEnabled));
        Notify(nameof(EqualizerConfig));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Persists the current session's progress to the media library (Continue Watching / resume).
    /// Only records once we have an actual, non-zero playback position so a resume point is never
    /// clobbered with zero. Never throws — recording must not break playback.
    /// </summary>
    private async Task RecordSessionAsync(TimeSpan? explicitPosition = null)
    {
        if (_libraryItemId is null || _repository is null)
        {
            return;
        }

        var position = explicitPosition ?? _engine.Position;
        var duration = _engine.Duration;
        if (position < TimeSpan.Zero)
        {
            position = TimeSpan.Zero;
        }
        if (position <= TimeSpan.Zero || duration <= TimeSpan.Zero)
        {
            return;
        }

        try
        {
            await _repository.RecordPlaybackAsync(_libraryItemId.Value, position, duration).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Playback session record failed: {ex}");
        }
    }

    /// <summary>
    /// Captures a cover-art still of the currently loaded library item (best-effort, one shot
    /// per load) and persists its path via <see cref="IMediaLibraryRepository.SetCoverArtPathAsync"/>.
    /// Never throws; a failed snapshot simply leaves the cover art unset for a later attempt.
    /// </summary>
    private async Task CaptureThumbnailForLibraryItemAsync()
    {
        var item = _libraryItem;
        if (item is null || _repository is null || string.IsNullOrEmpty(FilePath))
        {
            return;
        }

        try
        {
            var thumbsDir = _thumbnailDirectory ?? Path.Combine(
                DiagnosticReportLocations.DefaultDataDirectory(), "thumbs");
            Directory.CreateDirectory(thumbsDir);
            var outputPath = Path.Combine(thumbsDir, $"{item.Fingerprint}.jpg");

            var ok = await _engine.TakeSnapshotAsync(outputPath, 320, 0).ConfigureAwait(false);
            if (ok && File.Exists(outputPath))
            {
                await _repository.SetCoverArtPathAsync(item.Id, outputPath).ConfigureAwait(false);
                item.CoverArtPath = outputPath;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Thumbnail capture failed: {ex}");
        }
    }

    private void NotifyAll()
    {
        Notify(nameof(StateLabel));
        Notify(nameof(IsPlaying));
        Notify(nameof(PositionPercent));
        Notify(nameof(PositionText));
        Notify(nameof(DurationText));
        Notify(nameof(Volume));
        Notify(nameof(IsMuted));
        Notify(nameof(Rate));
        Notify(nameof(RateText));
        Notify(nameof(Title));
        Notify(nameof(FilePath));
        Notify(nameof(Metadata));
        Notify(nameof(AudioTracks));
        Notify(nameof(SubtitleTracks));
        Notify(nameof(VideoTracks));
        Notify(nameof(SubtitleDelay));
        Notify(nameof(SubtitleDelayMilliseconds));
        Notify(nameof(SubtitleDelayText));
        Notify(nameof(AudioDelay));
        Notify(nameof(AudioDelayMilliseconds));
        Notify(nameof(AudioDelayText));
        Notify(nameof(IsEqualizerEnabled));
        Notify(nameof(EqualizerConfig));
        Notify(nameof(AudioOutputDevices));
        Notify(nameof(ActiveAudioOutputDeviceId));
        Notify(nameof(AudioChannel));
        Notify(nameof(IsLoudnessNormalizationEnabled));
        Notify(nameof(IsNightModeEnabled));
        Notify(nameof(SubtitleTextScalePercent));
        Notify(nameof(SubtitleVerticalMarginPx));
        Notify(nameof(SubtitleFontFamily));
        Notify(nameof(SubtitleTextColorArgb));
        Notify(nameof(SubtitleOutlineColorArgb));
        Notify(nameof(SubtitleOutline));
        Notify(nameof(SubtitleStyle));
        Notify(nameof(LoadedSubtitleDocument));
        Notify(nameof(CanConvert));
        Notify(nameof(Chapters));
        Notify(nameof(CanGenerateChapters));
        Notify(nameof(StereoBalanceValue));
        Notify(nameof(IsStereoBalanceActive));
        Notify(nameof(CanUseStereoBalance));
    }

    private static string FormatDelay(TimeSpan delay)
    {
        var ms = (int)Math.Round(delay.TotalMilliseconds);
        if (ms == 0) return "0 ms";
        return ms > 0 ? $"+{ms} ms" : $"{ms} ms";
    }

    private static string FormatTime(TimeSpan ts)
    {
        if (ts < TimeSpan.Zero) ts = TimeSpan.Zero;
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";
    }

    // ── IDisposable ─────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _engine.StateChanged -= OnStateChanged;
        _engine.PositionChanged -= OnPositionChanged;
        _engine.ErrorOccurred -= OnErrorOccurred;
        _engine.TracksChanged -= OnTracksChanged;
        _engine.EqualizerChanged -= OnEqualizerChanged;
        _engine.EndReached -= OnEngineEndReached;
        _ = RecordSessionAsync();
        _ = DisableStereoBalanceAsync();
    }
}
