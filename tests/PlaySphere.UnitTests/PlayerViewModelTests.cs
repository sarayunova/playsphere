using PlaySphere.Application.AI;
using PlaySphere.Application.Library;
using PlaySphere.Application.Preferences;
using PlaySphere.Application.Visualizer;
using PlaySphere.Domain.AI;
using PlaySphere.Domain.Library;
using PlaySphere.Domain.Media;
using PlaySphere.Playback.Abstractions;
using PlaySphere.Subtitles;
using PlaySphere.UI.ViewModels;
using Xunit;

namespace PlaySphere.UnitTests;

public sealed class PlayerViewModelTests
{
    private sealed class FakePlaybackEngine : IMediaPlaybackEngine
    {
        public PlaybackState State { get; set; } = PlaybackState.Empty;
        public string? LoadedFilePath { get; set; }
        public TimeSpan Position { get; set; } = TimeSpan.Zero;
        public TimeSpan Duration { get; set; } = TimeSpan.Zero;
        public bool IsSeekable { get; set; } = true;
        public double Rate { get; set; } = 1.0;
        public int Volume { get; set; } = 100;
        public bool IsMuted { get; set; }
        public TimeSpan SubtitleDelay { get; set; } = TimeSpan.Zero;
        public TimeSpan AudioDelay { get; set; } = TimeSpan.Zero;
        public bool IsEqualizerEnabled { get; set; }
        public EqualizerConfiguration EqualizerConfig { get; set; } = EqualizerConfiguration.Default;
        public string? ActiveAudioOutputDeviceId { get; set; }
        public AudioChannelMode AudioChannel { get; set; } = AudioChannelMode.Stereo;
        public AudioEnhancementOptions AudioEnhancements { get; set; } = AudioEnhancementOptions.Default;
        public SubtitleStyleOptions SubtitleStyle { get; set; } = SubtitleStyleOptions.Default;
        public MediaMetadata Metadata { get; set; } = MediaMetadata.Empty;
        public List<AudioOutputDeviceInfo> FakeAudioOutputDevices { get; } = [];

        public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;
        public event EventHandler<PlaybackPositionChangedEventArgs>? PositionChanged;
        public event EventHandler<PlaybackEndedEventArgs>? EndReached;
        public event EventHandler<PlaybackErrorEventArgs>? ErrorOccurred;
        public event EventHandler<PlaybackTracksChangedEventArgs>? TracksChanged;
        public event EventHandler<PlaybackEqualizerChangedEventArgs>? EqualizerChanged;

        public bool PlayCalled { get; private set; }
        public bool PauseCalled { get; private set; }
        public bool StopCalled { get; private set; }
        public TimeSpan? LastSeekTarget { get; private set; }
        public int? LastSelectedAudioTrackId { get; private set; }
        public MediaFile? LastLoadedMedia { get; private set; }

        public void RaiseStateChanged(PlaybackState state)
        {
            var old = State;
            State = state;
            StateChanged?.Invoke(this, new PlaybackStateChangedEventArgs(old, state));
        }

        public void RaisePositionChanged(TimeSpan pos)
        {
            Position = pos;
            PositionChanged?.Invoke(this, new PlaybackPositionChangedEventArgs(pos));
        }

        public void RaiseErrorOccurred(string message, Exception? ex = null)
        {
            State = PlaybackState.Error;
            ErrorOccurred?.Invoke(this, new PlaybackErrorEventArgs(message, ex));
        }

        public void RaiseEndReached()
        {
            State = PlaybackState.Stopped;
            EndReached?.Invoke(this, new PlaybackEndedEventArgs());
        }

        public void RaiseTracksChanged(MediaMetadata metadata)
        {
            Metadata = metadata;
            TracksChanged?.Invoke(this, new PlaybackTracksChangedEventArgs(metadata));
        }

        public Task LoadAsync(MediaFile media, CancellationToken cancellationToken = default)
        {
            LastLoadedMedia = media;
            LoadedFilePath = media.FilePath;
            State = PlaybackState.Ready;
            return Task.CompletedTask;
        }

        public Task PlayAsync(CancellationToken cancellationToken = default)
        {
            PlayCalled = true;
            RaiseStateChanged(PlaybackState.Playing);
            return Task.CompletedTask;
        }

        public Task PauseAsync(CancellationToken cancellationToken = default)
        {
            PauseCalled = true;
            RaiseStateChanged(PlaybackState.Paused);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCalled = true;
            Position = TimeSpan.Zero;
            RaiseStateChanged(PlaybackState.Stopped);
            return Task.CompletedTask;
        }

        public Task SeekAsync(TimeSpan target, CancellationToken cancellationToken = default)
        {
            LastSeekTarget = target;
            Position = target;
            return Task.CompletedTask;
        }

        public Task SetRateAsync(double rate, CancellationToken cancellationToken = default)
        {
            Rate = rate;
            return Task.CompletedTask;
        }

        public Task SetVolumeAsync(int volume, CancellationToken cancellationToken = default)
        {
            Volume = volume;
            return Task.CompletedTask;
        }

        public Task SetMutedAsync(bool muted, CancellationToken cancellationToken = default)
        {
            IsMuted = muted;
            return Task.CompletedTask;
        }

        public Task SelectAudioTrackAsync(int? trackId, CancellationToken cancellationToken = default)
        {
            LastSelectedAudioTrackId = trackId;
            return Task.CompletedTask;
        }

        public Task SelectSubtitleTrackAsync(int? trackId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SetSubtitleDelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
        {
            SubtitleDelay = delay;
            return Task.CompletedTask;
        }
        public Task SetAudioDelayAsync(TimeSpan delay, CancellationToken cancellationToken = default)
        {
            AudioDelay = delay;
            return Task.CompletedTask;
        }
        public Task<int> LoadExternalSubtitleAsync(string subtitlePath, CancellationToken cancellationToken = default)
        {
            LastExternalSubtitlePath = subtitlePath;
            return Task.FromResult(99);
        }

        public string? LastExternalSubtitlePath { get; private set; }

        public Task SetEqualizerEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
        {
            IsEqualizerEnabled = enabled;
            EqualizerConfig = EqualizerConfig with { IsEnabled = enabled };
            EqualizerChanged?.Invoke(this, new PlaybackEqualizerChangedEventArgs(EqualizerConfig));
            return Task.CompletedTask;
        }

        public Task SetEqualizerPresetAsync(string presetName, CancellationToken cancellationToken = default)
        {
            var preset = EqualizerPresets.GetPresetOrDefault(presetName);
            EqualizerConfig = EqualizerConfig with { PreampGainDb = preset.PreampGainDb, BandGainsDb = preset.BandGainsDb, PresetName = preset.Name };
            EqualizerChanged?.Invoke(this, new PlaybackEqualizerChangedEventArgs(EqualizerConfig));
            return Task.CompletedTask;
        }

        public Task SetEqualizerBandGainAsync(uint bandIndex, float gainDb, CancellationToken cancellationToken = default)
        {
            var bands = EqualizerConfig.BandGainsDb.ToArray();
            bands[bandIndex] = gainDb;
            EqualizerConfig = EqualizerConfig with { BandGainsDb = bands, PresetName = "Custom" };
            EqualizerChanged?.Invoke(this, new PlaybackEqualizerChangedEventArgs(EqualizerConfig));
            return Task.CompletedTask;
        }

        public Task SetEqualizerPreampAsync(float preampDb, CancellationToken cancellationToken = default)
        {
            EqualizerConfig = EqualizerConfig with { PreampGainDb = preampDb };
            EqualizerChanged?.Invoke(this, new PlaybackEqualizerChangedEventArgs(EqualizerConfig));
            return Task.CompletedTask;
        }

        public Task ResetEqualizerAsync(CancellationToken cancellationToken = default)
        {
            EqualizerConfig = EqualizerConfiguration.Default;
            EqualizerChanged?.Invoke(this, new PlaybackEqualizerChangedEventArgs(EqualizerConfig));
            return Task.CompletedTask;
        }

        public IReadOnlyList<AudioOutputDeviceInfo> GetAudioOutputDevices() => FakeAudioOutputDevices;

        public Task SetAudioOutputDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
        {
            ActiveAudioOutputDeviceId = deviceId;
            return Task.CompletedTask;
        }

        public Task SetAudioChannelAsync(AudioChannelMode mode, CancellationToken cancellationToken = default)
        {
            AudioChannel = mode;
            return Task.CompletedTask;
        }

        public Task SetAudioEnhancementsAsync(AudioEnhancementOptions options, CancellationToken cancellationToken = default)
        {
            AudioEnhancements = options;
            return Task.CompletedTask;
        }

        public Task SetSubtitleStyleAsync(SubtitleStyleOptions style, CancellationToken cancellationToken = default)
        {
            SubtitleStyle = style;
            return Task.CompletedTask;
        }

        public bool IsVideoSurfaceAttached { get; private set; }
        public IVideoSurface? AttachedSurface { get; private set; }
        public int AttachCount { get; private set; }
        public int DetachCount { get; private set; }

        public class FakeVideoSurface : IVideoSurface
        {
            public FakeVideoSurface(string[]? swapChainOptions = null) => SwapChainOptions = swapChainOptions;
            public string[]? SwapChainOptions { get; }
            public object? CurrentRenderer { get; private set; }
            public void SetRenderer(object renderer) => CurrentRenderer = renderer;
            public void ClearRenderer() => CurrentRenderer = null;
        }

        public Task AttachVideoSurfaceAsync(IVideoSurface surface, CancellationToken cancellationToken = default)
        {
            AttachedSurface = surface;
            IsVideoSurfaceAttached = true;
            AttachCount++;
            return Task.CompletedTask;
        }

        public Task DetachVideoSurfaceAsync(CancellationToken cancellationToken = default)
        {
            IsVideoSurfaceAttached = false;
            AttachedSurface = null;
            DetachCount++;
            return Task.CompletedTask;
        }

        public bool IsVisualizerAttached { get; private set; }
        public IVisualizerAudioSource? AttachedVisualizerSource { get; private set; }
        public int VisualizerAttachCount { get; private set; }
        public int VisualizerDetachCount { get; private set; }

        public Task AttachVisualizerSourceAsync(IVisualizerAudioSource source, CancellationToken cancellationToken = default)
        {
            AttachedVisualizerSource = source;
            IsVisualizerAttached = true;
            VisualizerAttachCount++;
            return Task.CompletedTask;
        }

        public Task DetachVisualizerSourceAsync(CancellationToken cancellationToken = default)
        {
            IsVisualizerAttached = false;
            AttachedVisualizerSource = null;
            VisualizerDetachCount++;
            return Task.CompletedTask;
        }

        public bool TakeSnapshotResult { get; set; } = true;
        public bool WriteSnapshotFile { get; set; }
        public string? LastSnapshotOutputPath { get; private set; }

        public Task<bool> TakeSnapshotAsync(string outputPath, uint width = 0, uint height = 0, CancellationToken cancellationToken = default)
        {
            LastSnapshotOutputPath = outputPath;
            if (WriteSnapshotFile)
            {
                var dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllBytes(outputPath, new byte[] { 1, 2, 3, 4 });
            }
            return Task.FromResult(TakeSnapshotResult);
        }

        public void Dispose() { }
    }

    [Fact]
    public void Initial_state_reflects_empty_engine()
    {
        var engine = new FakePlaybackEngine();
        using var vm = new PlayerViewModel(engine);

        Assert.Equal("Empty", vm.StateLabel);
        Assert.False(vm.IsPlaying);
        Assert.Equal(0, vm.PositionPercent);
        Assert.Equal("0:00", vm.PositionText);
        Assert.Equal("0:00", vm.DurationText);
        Assert.Equal(100, vm.Volume);
        Assert.False(vm.IsMuted);
        Assert.Equal("1.0×", vm.RateText);
        Assert.Equal("Now Playing", vm.Title);
    }

    [Fact]
    public async Task PlayPauseAsync_toggles_between_play_and_pause()
    {
        var engine = new FakePlaybackEngine();
        using var vm = new PlayerViewModel(engine);

        // Initially Empty -> calls PlayAsync
        await vm.PlayPauseAsync();
        Assert.True(engine.PlayCalled);
        Assert.True(vm.IsPlaying);
        Assert.Equal("Playing", vm.StateLabel);

        // While Playing -> calls PauseAsync
        await vm.PlayPauseAsync();
        Assert.True(engine.PauseCalled);
        Assert.False(vm.IsPlaying);
        Assert.Equal("Paused", vm.StateLabel);
    }

    [Fact]
    public void Buffering_state_surfaces_as_not_playing_with_buffering_label()
    {
        var engine = new FakePlaybackEngine();
        using var vm = new PlayerViewModel(engine);

        // libVLC reports Buffering during a network-stream stall (Phase 8). The stream is NOT
        // considered playing yet, and the on-screen state chip must read "Buffering".
        engine.RaiseStateChanged(PlaybackState.Buffering);

        Assert.False(vm.IsPlaying);
        Assert.Equal("Buffering", vm.StateLabel);
    }

    [Fact]
    public void Playing_then_buffering_then_playing_tracks_live_stalled_state()
    {
        var engine = new FakePlaybackEngine();
        using var vm = new PlayerViewModel(engine);

        engine.RaiseStateChanged(PlaybackState.Playing);
        Assert.True(vm.IsPlaying);

        // A stall drops to Buffering (auto-reconnect in progress) — not playing.
        engine.RaiseStateChanged(PlaybackState.Buffering);
        Assert.False(vm.IsPlaying);
        Assert.Equal("Buffering", vm.StateLabel);

        // Reconnect succeeds — playback resumes.
        engine.RaiseStateChanged(PlaybackState.Playing);
        Assert.True(vm.IsPlaying);
        Assert.Equal("Playing", vm.StateLabel);
    }

    [Fact]
    public async Task StopAsync_stops_engine_and_updates_state()
    {
        var engine = new FakePlaybackEngine { State = PlaybackState.Playing };
        using var vm = new PlayerViewModel(engine);

        await vm.StopAsync();
        Assert.True(engine.StopCalled);
        Assert.Equal("Stopped", vm.StateLabel);
        Assert.False(vm.IsPlaying);
    }

    [Fact]
    public async Task SkipBackAsync_clamps_at_zero()
    {
        var engine = new FakePlaybackEngine
        {
            Position = TimeSpan.FromSeconds(5),
            Duration = TimeSpan.FromSeconds(100)
        };
        using var vm = new PlayerViewModel(engine);

        await vm.SkipBackAsync();
        Assert.Equal(TimeSpan.Zero, engine.LastSeekTarget);
    }

    [Fact]
    public async Task SkipForwardAsync_clamps_at_duration()
    {
        var engine = new FakePlaybackEngine
        {
            Position = TimeSpan.FromSeconds(95),
            Duration = TimeSpan.FromSeconds(100)
        };
        using var vm = new PlayerViewModel(engine);

        await vm.SkipForwardAsync();
        Assert.Equal(TimeSpan.FromSeconds(100), engine.LastSeekTarget);
    }

    [Fact]
    public async Task SeekToPercentAsync_computes_exact_timestamp()
    {
        var engine = new FakePlaybackEngine
        {
            Duration = TimeSpan.FromMinutes(2) // 120 s
        };
        using var vm = new PlayerViewModel(engine);

        await vm.SeekToPercentAsync(50.0);
        Assert.Equal(TimeSpan.FromSeconds(60), engine.LastSeekTarget);
    }

    [Fact]
    public async Task Volume_and_Mute_update_engine_and_properties()
    {
        var engine = new FakePlaybackEngine();
        using var vm = new PlayerViewModel(engine);

        await vm.SetVolumeAsync(75);
        Assert.Equal(75, engine.Volume);
        Assert.Equal(75, vm.Volume);

        await vm.ToggleMuteAsync();
        Assert.True(engine.IsMuted);
        Assert.True(vm.IsMuted);

        await vm.ToggleMuteAsync();
        Assert.False(engine.IsMuted);
        Assert.False(vm.IsMuted);
    }

    [Fact]
    public void Position_formatting_handles_hours_correctly()
    {
        var engine = new FakePlaybackEngine
        {
            Duration = TimeSpan.FromHours(1.5) // 1:30:00
        };
        using var vm = new PlayerViewModel(engine);

        engine.RaisePositionChanged(TimeSpan.FromMinutes(65).Add(TimeSpan.FromSeconds(23))); // 1:05:23
        Assert.Equal("1:05:23", vm.PositionText);
    }

    [Fact]
    public void BeginSeek_and_EndSeek_suppress_position_updates()
    {
        var engine = new FakePlaybackEngine
        {
            Duration = TimeSpan.FromSeconds(100)
        };
        using var vm = new PlayerViewModel(engine);

        var propertyChangedFired = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PlayerViewModel.PositionPercent))
                propertyChangedFired = true;
        };

        vm.BeginSeek();
        engine.RaisePositionChanged(TimeSpan.FromSeconds(50));
        Assert.False(propertyChangedFired, "Position update should be suppressed while seeking");

        vm.EndSeek();
        engine.RaisePositionChanged(TimeSpan.FromSeconds(60));
        Assert.True(propertyChangedFired, "Position update should fire after seeking ends");
    }

    [Fact]
    public void Error_event_updates_state_label_and_captures_message()
    {
        var engine = new FakePlaybackEngine();
        using var vm = new PlayerViewModel(engine);

        engine.RaiseErrorOccurred("Demuxer failure");
        Assert.Equal("Error", vm.StateLabel);
        Assert.Equal("Demuxer failure", vm.LastError);

        // A transition out of the Error state clears the message so it doesn't linger on screen.
        engine.RaiseStateChanged(PlaybackState.Paused);
        Assert.Null(vm.LastError);
    }

    [Fact]
    public async Task LoadAsync_sets_title_from_media()
    {
        var engine = new FakePlaybackEngine();
        using var vm = new PlayerViewModel(engine);

        await vm.LoadAsync("C:\\media\\testcard.mp4");
        Assert.Equal("testcard", vm.Title);
        Assert.NotNull(engine.LastLoadedMedia);
    }

    [Fact]
    public async Task LoadAsync_with_url_builds_a_remote_media_file()
    {
        var engine = new FakePlaybackEngine();
        using var vm = new PlayerViewModel(engine);

        await vm.LoadAsync("https://cdn.example.com/live/concert.ts");
        Assert.True(engine.LastLoadedMedia?.IsRemoteUri);
        Assert.Equal("concert", vm.Title);
        Assert.Equal("https://cdn.example.com/live/concert.ts", engine.LastLoadedMedia!.FilePath);
    }

    [Fact]
    public async Task SeekRelativeAsync_moves_forward_and_backward_within_bounds()
    {
        var engine = new FakePlaybackEngine
        {
            Position = TimeSpan.FromSeconds(50),
            Duration = TimeSpan.FromSeconds(100)
        };
        using var vm = new PlayerViewModel(engine);

        // Relative seek forward +5s
        await vm.SeekRelativeAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(TimeSpan.FromSeconds(55), engine.LastSeekTarget);

        // Relative seek backward -30s
        await vm.SeekRelativeAsync(TimeSpan.FromSeconds(-30));
        Assert.Equal(TimeSpan.FromSeconds(25), engine.LastSeekTarget);

        // Clamp at start (negative)
        await vm.SeekRelativeAsync(TimeSpan.FromSeconds(-100));
        Assert.Equal(TimeSpan.Zero, engine.LastSeekTarget);

        // Clamp at end (beyond duration)
        await vm.SeekRelativeAsync(TimeSpan.FromSeconds(200));
        Assert.Equal(TimeSpan.FromSeconds(100), engine.LastSeekTarget);
    }

    [Fact]
    public void GetTimestampForPercent_and_GetHoverPreviewText_compute_accurately()
    {
        var engine = new FakePlaybackEngine
        {
            Duration = TimeSpan.FromMinutes(4) // 240 s
        };
        using var vm = new PlayerViewModel(engine);

        Assert.Equal(TimeSpan.Zero, vm.GetTimestampForPercent(0));
        Assert.Equal(TimeSpan.FromMinutes(1), vm.GetTimestampForPercent(25));
        Assert.Equal(TimeSpan.FromMinutes(2), vm.GetTimestampForPercent(50));
        Assert.Equal(TimeSpan.FromMinutes(4), vm.GetTimestampForPercent(100));

        Assert.Equal("1:00 / 4:00", vm.GetHoverPreviewText(25));
        Assert.Equal("2:00 / 4:00", vm.GetHoverPreviewText(50));
    }

    [Fact]
    public async Task CycleRateAsync_cycles_through_rates_in_sequence()
    {
        var engine = new FakePlaybackEngine { Rate = 1.0 };
        using var vm = new PlayerViewModel(engine);

        Assert.Equal("1.0×", vm.RateText);

        await vm.CycleRateAsync();
        Assert.Equal(1.25, engine.Rate);
        Assert.Equal("1.2×", vm.RateText);

        await vm.CycleRateAsync();
        Assert.Equal(1.5, engine.Rate);

        await vm.CycleRateAsync();
        Assert.Equal(2.0, engine.Rate);

        await vm.CycleRateAsync();
        Assert.Equal(0.5, engine.Rate);

        await vm.CycleRateAsync();
        Assert.Equal(1.0, engine.Rate);
    }

    [Fact]
    public void Drawer_open_close_toggle_manage_state()
    {
        var engine = new FakePlaybackEngine();
        using var vm = new PlayerViewModel(engine);

        Assert.Equal(PlaySphere.UI.Models.PlayerDrawerKind.None, vm.ActiveDrawer);
        Assert.False(vm.IsDrawerOpen);

        vm.OpenDrawer(PlaySphere.UI.Models.PlayerDrawerKind.AudioTracks);
        Assert.Equal(PlaySphere.UI.Models.PlayerDrawerKind.AudioTracks, vm.ActiveDrawer);
        Assert.True(vm.IsDrawerOpen);

        vm.ToggleDrawer(PlaySphere.UI.Models.PlayerDrawerKind.AudioTracks);
        Assert.Equal(PlaySphere.UI.Models.PlayerDrawerKind.None, vm.ActiveDrawer);
        Assert.False(vm.IsDrawerOpen);

        vm.ToggleDrawer(PlaySphere.UI.Models.PlayerDrawerKind.MediaInfo);
        Assert.Equal(PlaySphere.UI.Models.PlayerDrawerKind.MediaInfo, vm.ActiveDrawer);
        Assert.True(vm.IsDrawerOpen);

        vm.CloseDrawer();
        Assert.Equal(PlaySphere.UI.Models.PlayerDrawerKind.None, vm.ActiveDrawer);
        Assert.False(vm.IsDrawerOpen);
    }

    [Fact]
    public async Task Track_enumeration_and_selection_work()
    {
        var engine = new FakePlaybackEngine();
        using var vm = new PlayerViewModel(engine);

        var meta = new MediaMetadata(
            Duration: TimeSpan.FromMinutes(5),
            Container: "Matroska",
            VideoTracks: new[] { new VideoTrackInfo(0, "und", "Video", "h264", "1920x1080") },
            AudioTracks: new[] { new AudioTrackInfo(1, "eng", "English", "aac", 48000, 2), new AudioTrackInfo(2, "spa", "Spanish", "ac3", 48000, 6) },
            SubtitleTracks: new[] { new SubtitleTrackInfo(1, "eng", "English", "subrip") }
        );

        engine.RaiseTracksChanged(meta);

        Assert.Single(vm.VideoTracks);
        Assert.Equal(2, vm.AudioTracks.Count);
        Assert.Single(vm.SubtitleTracks);

        await vm.SelectAudioTrackAsync(2);
        Assert.Equal(2, vm.SelectedAudioTrackId);

        await vm.SelectSubtitleTrackAsync(1);
        Assert.Equal(1, vm.SelectedSubtitleTrackId);

        await vm.SelectSubtitleTrackAsync(null);
        Assert.Null(vm.SelectedSubtitleTrackId);
    }

    [Fact]
    public async Task SetRateAsync_clamps_and_updates_rate()
    {
        var engine = new FakePlaybackEngine();
        using var vm = new PlayerViewModel(engine);

        await vm.SetRateAsync(1.75);
        Assert.Equal(1.75, engine.Rate);
        Assert.Equal("1.8×", vm.RateText);

        // Clamp minimum
        await vm.SetRateAsync(0.1);
        Assert.Equal(0.25, engine.Rate);

        // Clamp maximum
        await vm.SetRateAsync(10.0);
        Assert.Equal(4.0, engine.Rate);
    }

    [Fact]
    public async Task LoadExternalSubtitleAsync_updates_selected_subtitle_track()
    {
        var engine = new FakePlaybackEngine();
        using var vm = new PlayerViewModel(engine);

        var trackId = await vm.LoadExternalSubtitleAsync("C:\\media\\movie.srt");
        Assert.Equal(99, trackId);
        Assert.Equal(99, vm.SelectedSubtitleTrackId);
        Assert.Equal("C:\\media\\movie.srt", engine.LastExternalSubtitlePath);
    }

    [Fact]
    public async Task TryAutoLoadSubtitleAsync_finds_and_loads_matching_srt()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "PlaySphere_SubTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var mediaPath = Path.Combine(tempDir, "video.mp4");
            var srtPath = Path.Combine(tempDir, "video.srt");
            await File.WriteAllTextAsync(mediaPath, "dummy video data");
            await File.WriteAllTextAsync(srtPath, "1\n00:00:01,000 --> 00:00:04,000\nHello World\n");

            var engine = new FakePlaybackEngine();
            using var vm = new PlayerViewModel(engine);

            await vm.LoadAsync(mediaPath);

            Assert.Equal("video", vm.Title);
            Assert.Equal(mediaPath, vm.FilePath);
            Assert.Equal(99, vm.SelectedSubtitleTrackId);
            Assert.Equal(srtPath, engine.LastExternalSubtitlePath);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task TryAutoLoadSubtitleAsync_returns_minus_one_when_no_subtitle_matches()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "PlaySphere_SubTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var mediaPath = Path.Combine(tempDir, "standalone.mp4");
            await File.WriteAllTextAsync(mediaPath, "dummy video data");

            var engine = new FakePlaybackEngine();
            using var vm = new PlayerViewModel(engine);

            await vm.LoadAsync(mediaPath);

            Assert.Equal(-1, await vm.TryAutoLoadSubtitleAsync());
            Assert.Null(engine.LastExternalSubtitlePath);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task SubtitleDelay_properties_and_adjustments_work_correctly()
    {
        var engine = new FakePlaybackEngine();
        using var vm = new PlayerViewModel(engine);

        Assert.Equal(TimeSpan.Zero, vm.SubtitleDelay);
        Assert.Equal(0.0, vm.SubtitleDelayMilliseconds);
        Assert.Equal("0 ms", vm.SubtitleDelayText);

        // Positive delay (+150ms)
        await vm.SetSubtitleDelayAsync(TimeSpan.FromMilliseconds(150));
        Assert.Equal(TimeSpan.FromMilliseconds(150), engine.SubtitleDelay);
        Assert.Equal(150.0, vm.SubtitleDelayMilliseconds);
        Assert.Equal("+150 ms", vm.SubtitleDelayText);

        // Adjust delay (+50ms -> +200ms)
        await vm.AdjustSubtitleDelayAsync(TimeSpan.FromMilliseconds(50));
        Assert.Equal(TimeSpan.FromMilliseconds(200), engine.SubtitleDelay);
        Assert.Equal(200.0, vm.SubtitleDelayMilliseconds);
        Assert.Equal("+200 ms", vm.SubtitleDelayText);

        // Negative delay (-350ms -> -150ms)
        await vm.AdjustSubtitleDelayAsync(TimeSpan.FromMilliseconds(-350));
        Assert.Equal(TimeSpan.FromMilliseconds(-150), engine.SubtitleDelay);
        Assert.Equal(-150.0, vm.SubtitleDelayMilliseconds);
        Assert.Equal("-150 ms", vm.SubtitleDelayText);

        // Reset delay to zero
        await vm.ResetSubtitleDelayAsync();
        Assert.Equal(TimeSpan.Zero, engine.SubtitleDelay);
        Assert.Equal(0.0, vm.SubtitleDelayMilliseconds);
        Assert.Equal("0 ms", vm.SubtitleDelayText);
    }

    [Fact]
    public async Task AudioDelay_properties_and_adjustments_work_correctly()
    {
        var engine = new FakePlaybackEngine();
        using var vm = new PlayerViewModel(engine);

        Assert.Equal(TimeSpan.Zero, vm.AudioDelay);
        Assert.Equal(0.0, vm.AudioDelayMilliseconds);
        Assert.Equal("0 ms", vm.AudioDelayText);

        // Positive delay (+250ms)
        await vm.SetAudioDelayAsync(TimeSpan.FromMilliseconds(250));
        Assert.Equal(TimeSpan.FromMilliseconds(250), engine.AudioDelay);
        Assert.Equal(250.0, vm.AudioDelayMilliseconds);
        Assert.Equal("+250 ms", vm.AudioDelayText);

        // Adjust delay (+50ms -> +300ms)
        await vm.AdjustAudioDelayAsync(TimeSpan.FromMilliseconds(50));
        Assert.Equal(TimeSpan.FromMilliseconds(300), engine.AudioDelay);
        Assert.Equal(300.0, vm.AudioDelayMilliseconds);
        Assert.Equal("+300 ms", vm.AudioDelayText);

        // Negative delay (-400ms -> -100ms)
        await vm.AdjustAudioDelayAsync(TimeSpan.FromMilliseconds(-400));
        Assert.Equal(TimeSpan.FromMilliseconds(-100), engine.AudioDelay);
        Assert.Equal(-100.0, vm.AudioDelayMilliseconds);
        Assert.Equal("-100 ms", vm.AudioDelayText);

        // Reset delay to zero
        await vm.ResetAudioDelayAsync();
        Assert.Equal(TimeSpan.Zero, engine.AudioDelay);
        Assert.Equal(0.0, vm.AudioDelayMilliseconds);
        Assert.Equal("0 ms", vm.AudioDelayText);
    }

    [Fact]
    public void Engine_constructor_null_check()
    {
        Assert.Throws<ArgumentNullException>(() => new PlayerViewModel(null!));
    }

    [Fact]
    public async Task SetEqualizerEnabledAsync_updates_viewmodel_and_raises_property_changed()
    {
        var engine = new FakePlaybackEngine();
        using var vm = new PlayerViewModel(engine);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        await vm.SetEqualizerEnabledAsync(true);

        Assert.True(vm.IsEqualizerEnabled);
        Assert.Contains(nameof(PlayerViewModel.IsEqualizerEnabled), raised);
    }

    [Fact]
    public async Task SetEqualizerPresetAsync_applies_preset_to_engine()
    {
        var engine = new FakePlaybackEngine();
        using var vm = new PlayerViewModel(engine);

        await vm.SetEqualizerPresetAsync("Rock");

        Assert.Equal("Rock", vm.EqualizerConfig.PresetName);
    }

    [Fact]
    public async Task SetEqualizerBandGainAsync_and_PreampAsync_update_config()
    {
        var engine = new FakePlaybackEngine();
        using var vm = new PlayerViewModel(engine);

        await vm.SetEqualizerBandGainAsync(2, 6.5f);
        await vm.SetEqualizerPreampAsync(3.0f);

        Assert.Equal(6.5f, vm.EqualizerConfig.BandGainsDb[2]);
        Assert.Equal(3.0f, vm.EqualizerConfig.PreampGainDb);
    }

    [Fact]
    public async Task ResetEqualizerAsync_restores_default_configuration()
    {
        var engine = new FakePlaybackEngine();
        using var vm = new PlayerViewModel(engine);
        await vm.SetEqualizerPresetAsync("Rock");

        await vm.ResetEqualizerAsync();

        Assert.Equal(EqualizerConfiguration.Default, vm.EqualizerConfig);
    }

    [Fact]
    public void EqualizerPresetNames_exposes_catalog()
    {
        var engine = new FakePlaybackEngine();
        using var vm = new PlayerViewModel(engine);

        Assert.Contains("Flat", vm.EqualizerPresetNames);
        Assert.Contains("Rock", vm.EqualizerPresetNames);
    }

    [Fact]
    public async Task SetAudioOutputDeviceAsync_updates_active_device()
    {
        var engine = new FakePlaybackEngine();
        using var vm = new PlayerViewModel(engine);

        await vm.SetAudioOutputDeviceAsync("device-1");

        Assert.Equal("device-1", vm.ActiveAudioOutputDeviceId);
    }

    [Fact]
    public void AudioOutputDevices_reflects_engine_enumeration()
    {
        var engine = new FakePlaybackEngine();
        engine.FakeAudioOutputDevices.Add(new AudioOutputDeviceInfo("dev-a", "Speakers"));
        using var vm = new PlayerViewModel(engine);

        Assert.Single(vm.AudioOutputDevices);
        Assert.Equal("Speakers", vm.AudioOutputDevices[0].Name);
    }

    [Fact]
    public async Task SetAudioChannelAsync_updates_viewmodel()
    {
        var engine = new FakePlaybackEngine();
        using var vm = new PlayerViewModel(engine);

        await vm.SetAudioChannelAsync(AudioChannelMode.MonoLeft);

        Assert.Equal(AudioChannelMode.MonoLeft, vm.AudioChannel);
    }

    [Fact]
    public async Task Loudness_normalization_and_night_mode_toggle_independently()
    {
        var engine = new FakePlaybackEngine();
        using var vm = new PlayerViewModel(engine);

        await vm.SetLoudnessNormalizationEnabledAsync(true);
        Assert.True(vm.IsLoudnessNormalizationEnabled);
        Assert.False(vm.IsNightModeEnabled);

        await vm.SetNightModeEnabledAsync(true);
        Assert.True(vm.IsLoudnessNormalizationEnabled);
        Assert.True(vm.IsNightModeEnabled);

        await vm.SetLoudnessNormalizationEnabledAsync(false);
        Assert.False(vm.IsLoudnessNormalizationEnabled);
        Assert.True(vm.IsNightModeEnabled);
    }

    [Fact]
    public async Task SetSubtitleTextScalePercentAsync_updates_viewmodel()
    {
        var engine = new FakePlaybackEngine();
        using var vm = new PlayerViewModel(engine);

        await vm.SetSubtitleTextScalePercentAsync(150);

        Assert.Equal(150, vm.SubtitleTextScalePercent);
    }

    [Fact]
    public async Task SetSubtitleFontAsync_preserves_other_style_fields()
    {
        var engine = new FakePlaybackEngine();
        using var vm = new PlayerViewModel(engine);

        await vm.SetSubtitleFontAsync("Segoe UI");
        await vm.SetSubtitleColorAsync(0xFF0000);

        Assert.Equal("Segoe UI", engine.SubtitleStyle.FontFamily);
        Assert.Equal(0xFF0000, engine.SubtitleStyle.TextColorArgb);
        Assert.Equal(100, engine.SubtitleStyle.TextScalePercent);
        Assert.Equal(0, engine.SubtitleStyle.VerticalMarginPx);
    }

    [Fact]
    public async Task SetSubtitleStyleSetters_propagate_to_engine()
    {
        var engine = new FakePlaybackEngine();
        using var vm = new PlayerViewModel(engine);

        await vm.SetSubtitleOutlineAsync(SubtitleOutlineStyle.Normal);
        await vm.SetSubtitleOutlineColorAsync(0x00FF00);
        await vm.SetSubtitleVerticalMarginAsync(120);

        Assert.Equal(SubtitleOutlineStyle.Normal, engine.SubtitleStyle.Outline);
        Assert.Equal(0x00FF00, engine.SubtitleStyle.OutlineColorArgb);
        Assert.Equal(120, engine.SubtitleStyle.VerticalMarginPx);

        Assert.Equal(100, vm.SubtitleTextScalePercent);
        Assert.Equal(120, vm.SubtitleVerticalMarginPx);
        Assert.Equal(SubtitleOutlineStyle.Normal, vm.SubtitleOutline);
    }

    [Fact]
    public async Task SetSubtitleFontAsync_empty_preserves_default_null()
    {
        var engine = new FakePlaybackEngine();
        using var vm = new PlayerViewModel(engine);

        await vm.SetSubtitleFontAsync("   ");
        Assert.Null(engine.SubtitleStyle.FontFamily);

        await vm.SetSubtitleFontAsync("Arial");
        await vm.SetSubtitleFontAsync(null);
        Assert.Null(engine.SubtitleStyle.FontFamily);
    }

    [Fact]
    public async Task TryAutoLoadSubtitleAsync_finds_and_loads_matching_subtitle_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "PlaySphereViewModelAutoLoadTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var mediaPath = Path.Combine(dir, "Movie.mp4");
            var subtitlePath = Path.Combine(dir, "Movie.srt");
            File.WriteAllText(mediaPath, string.Empty);
            File.WriteAllText(subtitlePath, string.Empty);

            var engine = new FakePlaybackEngine();
            using var vm = new PlayerViewModel(engine);
            await vm.LoadAsync(mediaPath);

            Assert.Equal(subtitlePath, engine.LastExternalSubtitlePath);
            Assert.Equal(99, vm.SelectedSubtitleTrackId);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task LoadExternalSubtitleAsync_parses_srt_into_LoadedSubtitleDocument()
    {
        var dir = Path.Combine(Path.GetTempPath(), "PlaySphereViewModelCueTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var subtitlePath = Path.Combine(dir, "sample.srt");
            File.WriteAllText(subtitlePath, "1\n00:00:01,000 --> 00:00:02,000\nHello world\n");

            var engine = new FakePlaybackEngine();
            using var vm = new PlayerViewModel(engine);

            await vm.LoadExternalSubtitleAsync(subtitlePath);

            Assert.NotNull(vm.LoadedSubtitleDocument);
            Assert.Single(vm.LoadedSubtitleDocument!.Cues);
            Assert.Equal("Hello world", vm.LoadedSubtitleDocument.Cues[0].Text);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task SearchSubtitleCues_finds_matches_in_loaded_document()
    {
        var dir = Path.Combine(Path.GetTempPath(), "PlaySphereViewModelCueTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var subtitlePath = Path.Combine(dir, "sample.srt");
            File.WriteAllText(subtitlePath,
                "1\n00:00:01,000 --> 00:00:02,000\nfoo one\n\n2\n00:00:03,000 --> 00:00:04,000\nbar\n");

            var engine = new FakePlaybackEngine();
            using var vm = new PlayerViewModel(engine);
            await vm.LoadExternalSubtitleAsync(subtitlePath);

            var results = vm.SearchSubtitleCues("foo");

            Assert.Single(results);
            Assert.Equal("foo one", results[0].Text);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SearchSubtitleCues_returns_empty_when_no_document_loaded()
    {
        var engine = new FakePlaybackEngine();
        using var vm = new PlayerViewModel(engine);

        Assert.Empty(vm.SearchSubtitleCues("anything"));
    }

    [Fact]
    public async Task ExportLoadedSubtitleAsync_throws_when_no_document_loaded()
    {
        var engine = new FakePlaybackEngine();
        using var vm = new PlayerViewModel(engine);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => vm.ExportLoadedSubtitleAsync(Path.Combine(Path.GetTempPath(), "out.srt")));
    }

    [Fact]
    public async Task ExportLoadedSubtitleAsync_writes_parseable_srt_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "PlaySphereViewModelCueTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var subtitlePath = Path.Combine(dir, "sample.srt");
            File.WriteAllText(subtitlePath, "1\n00:00:01,000 --> 00:00:02,000\nHello world\n");
            var exportPath = Path.Combine(dir, "exported.srt");

            var engine = new FakePlaybackEngine();
            using var vm = new PlayerViewModel(engine);
            await vm.LoadExternalSubtitleAsync(subtitlePath);

            await vm.ExportLoadedSubtitleAsync(exportPath);

            Assert.True(File.Exists(exportPath));
            var reparsed = new SrtSubtitleParser().Parse(await File.ReadAllTextAsync(exportPath));
            Assert.Single(reparsed.Cues);
            Assert.Equal("Hello world", reparsed.Cues[0].Text);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task SeekToCueAsync_seeks_engine_to_cue_start()
    {
        var engine = new FakePlaybackEngine();
        using var vm = new PlayerViewModel(engine);
        var cue = new SubtitleCue(1, TimeSpan.FromSeconds(42), TimeSpan.FromSeconds(45), "text");

        await vm.SeekToCueAsync(cue);

        Assert.Equal(TimeSpan.FromSeconds(42), engine.LastSeekTarget);
    }

    [Fact]
    public async Task LoadAsync_clears_previous_LoadedSubtitleDocument()
    {
        var dir = Path.Combine(Path.GetTempPath(), "PlaySphereViewModelCueTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var mediaPath1 = Path.Combine(dir, "Movie1.mp4");
            var subtitlePath1 = Path.Combine(dir, "Movie1.srt");
            File.WriteAllText(mediaPath1, string.Empty);
            File.WriteAllText(subtitlePath1, "1\n00:00:01,000 --> 00:00:02,000\nHello\n");
            var mediaPath2 = Path.Combine(dir, "Movie2.mp4");
            File.WriteAllText(mediaPath2, string.Empty);

            var engine = new FakePlaybackEngine();
            using var vm = new PlayerViewModel(engine);
            await vm.LoadAsync(mediaPath1);
            Assert.NotNull(vm.LoadedSubtitleDocument);

            await vm.LoadAsync(mediaPath2);

            Assert.Null(vm.LoadedSubtitleDocument);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task TryAutoLoadSubtitleAsync_is_noop_when_no_subtitle_file_exists()
    {
        var dir = Path.Combine(Path.GetTempPath(), "PlaySphereViewModelAutoLoadTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var mediaPath = Path.Combine(dir, "Movie.mp4");
            File.WriteAllText(mediaPath, string.Empty);

            var engine = new FakePlaybackEngine();
            using var vm = new PlayerViewModel(engine);
            await vm.LoadAsync(mediaPath);

            Assert.Null(engine.LastExternalSubtitlePath);
            Assert.Null(vm.SelectedSubtitleTrackId);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task AddBookmarkAtCurrentAsync_adds_bookmark_for_cataloged_item()
    {
        var (repo, mediaPath, engine) = CreateCtx(resumePosition: TimeSpan.Zero, watchedPercentage: 0);

        using var vm = new PlayerViewModel(engine, repo);
        await vm.LoadAsync(mediaPath);

        Assert.True(vm.CanBookmark);

        engine.Position = TimeSpan.FromSeconds(45);
        var added = await vm.AddBookmarkAtCurrentAsync();

        Assert.True(added);
        var bookmark = Assert.Single(repo.Bookmarks);
        Assert.Equal(7, bookmark.MediaItemId);
        Assert.Equal(45000, bookmark.PositionMs);
        Assert.Single(vm.Bookmarks);
    }

    [Fact]
    public async Task AddBookmarkAtCurrentAsync_returns_false_when_file_not_in_library()
    {
        var repo = new FakeLibraryRepository();
        var engine = new FakePlaybackEngine();
        var (mediaPath, dir) = CreateTempMedia();

        using var vm = new PlayerViewModel(engine, repo);
        await vm.LoadAsync(mediaPath);

        Assert.False(vm.CanBookmark);
        Assert.False(await vm.AddBookmarkAtCurrentAsync());
        Assert.Empty(vm.Bookmarks);

        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task LoadBookmarksAsync_loads_bookmarks_for_cataloged_item()
    {
        var (repo, mediaPath, engine) = CreateCtx(resumePosition: TimeSpan.Zero, watchedPercentage: 0);
        repo.Bookmarks.Add(new Bookmark { Id = 1, MediaItemId = 7, PositionMs = 12000, Label = "intro" });

        using var vm = new PlayerViewModel(engine, repo);
        await vm.LoadAsync(mediaPath);
        await vm.LoadBookmarksAsync();

        var bookmark = Assert.Single(vm.Bookmarks);
        Assert.Equal("intro", bookmark.Label);
        Assert.Equal(12000, bookmark.PositionMs);
    }

    [Fact]
    public async Task RemoveBookmarkAsync_removes_bookmark_from_repository()
    {
        var (repo, mediaPath, engine) = CreateCtx(resumePosition: TimeSpan.Zero, watchedPercentage: 0);

        using var vm = new PlayerViewModel(engine, repo);
        await vm.LoadAsync(mediaPath);

        engine.Position = TimeSpan.FromSeconds(9);
        await vm.AddBookmarkAtCurrentAsync();
        var bookmark = Assert.Single(vm.Bookmarks);

        await vm.RemoveBookmarkAsync(bookmark.Id);

        Assert.Empty(repo.Bookmarks);
        Assert.Empty(vm.Bookmarks);
    }

    [Fact]
    public async Task SeekToBookmarkAsync_seeks_engine_to_bookmark_position()
    {
        var (repo, mediaPath, engine) = CreateCtx(resumePosition: TimeSpan.Zero, watchedPercentage: 0);
        engine.Duration = TimeSpan.FromSeconds(300);

        using var vm = new PlayerViewModel(engine, repo);
        await vm.LoadAsync(mediaPath);

        var bookmark = new Bookmark { Id = 5, MediaItemId = 7, PositionMs = 75000 };
        await vm.SeekToBookmarkAsync(bookmark);

        Assert.Equal(TimeSpan.FromSeconds(75), engine.LastSeekTarget);
    }

    [Fact]
    public async Task SeekToPositionAsync_seeks_engine_to_the_requested_position()
    {
        var (repo, mediaPath, engine) = CreateCtx(resumePosition: TimeSpan.Zero, watchedPercentage: 0);
        engine.Duration = TimeSpan.FromSeconds(300);

        using var vm = new PlayerViewModel(engine, repo);
        await vm.LoadAsync(mediaPath);

        await vm.SeekToPositionAsync(TimeSpan.FromSeconds(42));

        Assert.Equal(TimeSpan.FromSeconds(42), engine.LastSeekTarget);
    }

    [Fact]
    public async Task SeekToPositionAsync_clamps_at_duration()
    {
        var (repo, mediaPath, engine) = CreateCtx(resumePosition: TimeSpan.Zero, watchedPercentage: 0);
        engine.Duration = TimeSpan.FromSeconds(100);

        using var vm = new PlayerViewModel(engine, repo);
        await vm.LoadAsync(mediaPath);

        await vm.SeekToPositionAsync(TimeSpan.FromSeconds(500));

        Assert.Equal(TimeSpan.FromSeconds(100), engine.LastSeekTarget);
    }

    [Fact]
    public async Task SeekToPositionAsync_is_a_noop_when_not_seekable()
    {
        var (repo, mediaPath, engine) = CreateCtx(resumePosition: TimeSpan.Zero, watchedPercentage: 0);
        engine.IsSeekable = false;
        engine.Duration = TimeSpan.FromSeconds(100);

        using var vm = new PlayerViewModel(engine, repo);
        await vm.LoadAsync(mediaPath);

        await vm.SeekToPositionAsync(TimeSpan.FromSeconds(30));

        Assert.Null(engine.LastSeekTarget);
    }

    private sealed class FakeLibraryRepository : IMediaLibraryRepository
    {
        public readonly record struct Recorded(int MediaItemId, TimeSpan Position, TimeSpan Duration);

        public List<LibraryMediaItem> Items { get; } = new();
        public List<Recorded> Records { get; } = new();
        private readonly TaskCompletionSource _signal = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Add(LibraryMediaItem item) => Items.Add(item);

        public Task<LibraryMediaItem?> FindByPathAsync(string filePath, CancellationToken cancellationToken = default)
            => Task.FromResult(Items.FirstOrDefault(i => string.Equals(i.FilePath, filePath, StringComparison.OrdinalIgnoreCase)));

        public Task RecordPlaybackAsync(int mediaItemId, TimeSpan position, TimeSpan duration, CancellationToken cancellationToken = default)
        {
            Records.Add(new Recorded(mediaItemId, position, duration));
            _signal.TrySetResult();
            return Task.CompletedTask;
        }

        public Task WaitForRecordAsync()
        {
            if (Records.Count > 0) return Task.CompletedTask;
            return _signal.Task;
        }

        public int? SetCoverArtItemId { get; private set; }
        public string? SetCoverArtPathValue { get; private set; }

        public Task SetCoverArtPathAsync(int mediaItemId, string? coverArtPath, CancellationToken cancellationToken = default)
        {
            SetCoverArtItemId = mediaItemId;
            SetCoverArtPathValue = coverArtPath;
            return Task.CompletedTask;
        }

        public Task<LibraryFolder> AddFolderAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LibraryFolder> EnsureFolderAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RemoveFolderAsync(int folderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<LibraryFolder>> GetFoldersAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LibraryMediaItem> UpsertMediaItemAsync(LibraryMediaItem item, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LibraryMediaItem?> FindByFingerprintAsync(string fingerprint, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RemoveMediaItemAsync(int mediaItemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<LibraryMediaItem>> GetAllMediaItemsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<LibraryMediaItem>> GetFavoritesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<LibraryMediaItem>> GetContinueWatchingAsync(int maxCount = 20, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<LibraryMediaItem>> GetRecentlyPlayedAsync(int maxCount = 20, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<LibraryMediaItem>> SearchAsync(string query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetFavoriteAsync(int mediaItemId, bool isFavorite, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public List<Bookmark> Bookmarks { get; } = new();
        private int _nextBookmarkId = 1;
        private readonly object _bookmarkLock = new();

        public List<AiChapterEntity> Chapters { get; } = new();
        private int _nextChapterId = 1;
        private readonly object _chapterLock = new();

        public Task<Bookmark> AddBookmarkAsync(int mediaItemId, long positionMs, string? label = null, string? note = null, string? category = null, CancellationToken cancellationToken = default)
        {
            lock (_bookmarkLock)
            {
                var bookmark = new Bookmark
                {
                    Id = _nextBookmarkId++,
                    MediaItemId = mediaItemId,
                    PositionMs = Math.Max(0, positionMs),
                    Label = label,
                    Note = note,
                    Category = category,
                    CreatedAt = DateTime.UtcNow,
                };
                Bookmarks.Add(bookmark);
                return Task.FromResult(bookmark);
            }
        }

        public Task<IReadOnlyList<Bookmark>> GetBookmarksAsync(int mediaItemId, CancellationToken cancellationToken = default)
        {
            lock (_bookmarkLock)
            {
                return Task.FromResult<IReadOnlyList<Bookmark>>(Bookmarks.Where(b => b.MediaItemId == mediaItemId).ToList());
            }
        }

        public Task RemoveBookmarkAsync(int bookmarkId, CancellationToken cancellationToken = default)
        {
            lock (_bookmarkLock)
            {
                Bookmarks.RemoveAll(b => b.Id == bookmarkId);
            }
            return Task.CompletedTask;
        }
        public Task<Playlist> CreatePlaylistAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RenamePlaylistAsync(int playlistId, string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeletePlaylistAsync(int playlistId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Playlist>> GetPlaylistsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PlaylistItem> AddToPlaylistAsync(int playlistId, int mediaItemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddRangeToPlaylistAsync(int playlistId, IReadOnlyList<int> mediaItemIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RemoveFromPlaylistAsync(int playlistItemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ReorderPlaylistAsync(int playlistId, IReadOnlyList<int> mediaItemIdsInOrder, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<PlaylistItem>> GetPlaylistItemsAsync(int playlistId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AiTranscript> SaveTranscriptAsync(AiTranscript transcript, IReadOnlyList<AiTranscriptSegment> segments, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AiTranscript?> GetTranscriptAsync(int mediaItemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteTranscriptAsync(int mediaItemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SaveChaptersAsync(int mediaItemId, IReadOnlyList<AiChapterEntity> chapters, CancellationToken cancellationToken = default)
        {
            lock (_chapterLock)
            {
                Chapters.RemoveAll(c => c.MediaItemId == mediaItemId);
                foreach (var c in chapters)
                {
                    var copy = new AiChapterEntity
                    {
                        Id = _nextChapterId++,
                        MediaItemId = mediaItemId,
                        Title = c.Title,
                        StartSeconds = c.StartSeconds,
                        CreatedAt = c.CreatedAt,
                    };
                    Chapters.Add(copy);
                }
            }
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AiChapterEntity>> GetChaptersAsync(int mediaItemId, CancellationToken cancellationToken = default)
        {
            lock (_chapterLock)
            {
                return Task.FromResult<IReadOnlyList<AiChapterEntity>>(
                    Chapters.Where(c => c.MediaItemId == mediaItemId).OrderBy(c => c.StartSeconds).ToList());
            }
        }

        public Task<AiArtifact> SaveArtifactAsync(AiArtifact artifact, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AiArtifact>> GetArtifactsAsync(int mediaItemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteArtifactsAsync(int mediaItemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakePreferencesStore : IPreferencesStore
    {
        public List<string> Languages { get; } = new();
        public int SetCount { get; private set; }

        public IReadOnlyList<string> GetPreferredAudioLanguages() => Languages;

        public void SetPreferredAudioLanguages(IReadOnlyList<string> languages)
        {
            SetCount++;
            Languages.Clear();
            Languages.AddRange(languages);
        }
    }

    [Fact]
    public async Task LoadAsync_resumes_unfinished_library_item_to_last_position()
    {
        var (repo, mediaPath, engine) = CreateCtx(resumePosition: TimeSpan.FromSeconds(30), watchedPercentage: 50);

        using var vm = new PlayerViewModel(engine, repo);
        await vm.LoadAsync(mediaPath);

        Assert.Equal(TimeSpan.FromSeconds(30), engine.LastSeekTarget);
    }

    [Fact]
    public async Task LoadAsync_does_not_resume_when_file_is_not_in_library()
    {
        var repo = new FakeLibraryRepository();
        var engine = new FakePlaybackEngine();
        var (mediaPath, dir) = CreateTempMedia();

        using var vm = new PlayerViewModel(engine, repo);
        await vm.LoadAsync(mediaPath);

        Assert.Null(engine.LastSeekTarget);
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task LoadAsync_does_not_resume_finished_item()
    {
        var (repo, mediaPath, engine) = CreateCtx(resumePosition: TimeSpan.FromSeconds(30), watchedPercentage: 100);

        using var vm = new PlayerViewModel(engine, repo);
        await vm.LoadAsync(mediaPath);

        Assert.Null(engine.LastSeekTarget);
    }

    [Fact]
    public async Task StopAsync_records_final_progress_to_library()
    {
        var (repo, mediaPath, engine) = CreateCtx(resumePosition: TimeSpan.Zero, watchedPercentage: 0);
        engine.Duration = TimeSpan.FromSeconds(300);

        using var vm = new PlayerViewModel(engine, repo);
        await vm.LoadAsync(mediaPath);
        engine.RaisePositionChanged(TimeSpan.FromSeconds(90));

        await vm.StopAsync();

        var recorded = Assert.Single(repo.Records);
        Assert.Equal(7, recorded.MediaItemId);
        Assert.Equal(TimeSpan.FromSeconds(90), recorded.Position);
        Assert.Equal(TimeSpan.FromSeconds(300), recorded.Duration);
    }

    [Fact]
    public async Task EndReached_records_final_position_to_library()
    {
        var (repo, mediaPath, engine) = CreateCtx(TimeSpan.Zero, 0);
        engine.Duration = TimeSpan.FromSeconds(300);

        using var vm = new PlayerViewModel(engine, repo);
        await vm.LoadAsync(mediaPath);
        engine.RaisePositionChanged(TimeSpan.FromSeconds(150));
        engine.RaiseEndReached();

        await repo.WaitForRecordAsync();
        var recorded = Assert.Single(repo.Records);
        Assert.Equal(7, recorded.MediaItemId);
        Assert.Equal(TimeSpan.FromSeconds(300), recorded.Position);
        Assert.Equal(TimeSpan.FromSeconds(300), recorded.Duration);
    }

    [Fact]
    public async Task SeekToPercentAsync_after_end_restarts_playback()
    {
        var (repo, mediaPath, engine) = CreateCtx(TimeSpan.Zero, 0);
        engine.Duration = TimeSpan.FromSeconds(600); // 10 min

        using var vm = new PlayerViewModel(engine, repo);
        await vm.LoadAsync(mediaPath);

        // No end-reached yet: a plain scrub must NOT auto-play.
        await vm.SeekToPercentAsync(50.0);
        Assert.False(engine.PlayCalled);
        Assert.Equal(TimeSpan.FromSeconds(300), engine.LastSeekTarget);

        // Media finishes, then the user scrubs: playback must restart so the video actually
        // renders/goes there instead of staying frozen on the "completed" frame.
        engine.RaiseEndReached();
        await vm.SeekToPercentAsync(10.0);
        Assert.True(engine.PlayCalled, "seeking after end-reached should restart playback");
        Assert.Equal(TimeSpan.FromSeconds(60), engine.LastSeekTarget);
    }

    [Fact]
    public async Task Dispose_records_best_effort_progress_to_library()
    {
        var (repo, mediaPath, engine) = CreateCtx(TimeSpan.Zero, 0);
        engine.Duration = TimeSpan.FromSeconds(300);

        var vm = new PlayerViewModel(engine, repo);
        await vm.LoadAsync(mediaPath);
        engine.RaisePositionChanged(TimeSpan.FromSeconds(120));

        vm.Dispose();

        await repo.WaitForRecordAsync();
        var recorded = Assert.Single(repo.Records);
        Assert.Equal(7, recorded.MediaItemId);
        Assert.Equal(TimeSpan.FromSeconds(120), recorded.Position);
    }

    [Fact]
    public async Task CaptureThumbnail_persists_cover_art_when_frame_renders()
    {
        var (repo, mediaPath, engine) = CreateCtx(TimeSpan.Zero, 0);
        engine.WriteSnapshotFile = true;
        var thumbsDir = Path.Combine(Path.GetTempPath(), "PlaySphereThumb_" + Guid.NewGuid().ToString("N"));

        try
        {
            var vm = new PlayerViewModel(engine, repo, thumbsDir);
            await vm.LoadAsync(mediaPath);
            Assert.Null(repo.SetCoverArtPathValue);

            await vm.PlayPauseAsync();
            engine.RaisePositionChanged(TimeSpan.FromSeconds(2));

            Assert.NotNull(engine.LastSnapshotOutputPath);
            Assert.EndsWith($"{repo.Items[0].Fingerprint}.jpg", engine.LastSnapshotOutputPath!);
            Assert.Equal(7, repo.SetCoverArtItemId);
            Assert.NotNull(repo.SetCoverArtPathValue);
            Assert.EndsWith(".jpg", repo.SetCoverArtPathValue!);
            Assert.True(File.Exists(repo.SetCoverArtPathValue));
        }
        finally
        {
            if (Directory.Exists(thumbsDir)) Directory.Delete(thumbsDir, recursive: true);
        }
    }

    [Fact]
    public async Task CaptureThumbnail_skips_when_item_already_has_cover_art()
    {
        var (repo, mediaPath, engine) = CreateCtx(TimeSpan.Zero, 0);
        engine.WriteSnapshotFile = true;
        var thumbsDir = Path.Combine(Path.GetTempPath(), "PlaySphereThumb_" + Guid.NewGuid().ToString("N"));
        repo.Items[0].CoverArtPath = "already.png";

        try
        {
            var vm = new PlayerViewModel(engine, repo, thumbsDir);
            await vm.LoadAsync(mediaPath);
            await vm.PlayPauseAsync();
            engine.RaisePositionChanged(TimeSpan.FromSeconds(2));

            Assert.Null(engine.LastSnapshotOutputPath);
            Assert.Null(repo.SetCoverArtPathValue);
        }
        finally
        {
            if (Directory.Exists(thumbsDir)) Directory.Delete(thumbsDir, recursive: true);
        }
    }

    private sealed class FakeClipExporter : PlaySphere.Application.Media.IMediaClipExporter
    {
        public PlaySphere.Domain.Media.ClipExportResult Result { get; set; } = PlaySphere.Domain.Media.ClipExportResult.Ok;

        public string? LastInputPath { get; private set; }
        public string? LastOutputPath { get; private set; }
        public TimeSpan LastStart { get; private set; }
        public TimeSpan LastDuration { get; private set; }

        public Task<PlaySphere.Domain.Media.ClipExportResult> ExportClipAsync(
            string inputPath, string outputPath, TimeSpan start, TimeSpan duration, CancellationToken cancellationToken = default, IProgress<TimeSpan>? progress = null)
        {
            LastInputPath = inputPath;
            LastOutputPath = outputPath;
            LastStart = start;
            LastDuration = duration;
            return Task.FromResult(Result);
        }
    }

    // ── Phase 7 (PS-702): Mark In/Out + clip export ─────────────────────────

    [Fact]
    public void MarkIn_then_MarkOut_marks_a_valid_range()
    {
        var engine = new FakePlaybackEngine
        {
            LoadedFilePath = @"C:\media\video.mp4",
            Duration = TimeSpan.FromMinutes(5),
            IsSeekable = true,
            Metadata = MetadataWithVideo()
        };
        using var vm = new PlayerViewModel(engine, clipExporter: new FakeClipExporter());

        engine.Position = TimeSpan.FromMinutes(1);
        vm.SetMarkIn();
        engine.Position = TimeSpan.FromMinutes(4);
        vm.SetMarkOut();

        Assert.Equal(TimeSpan.FromMinutes(1), vm.MarkIn);
        Assert.Equal(TimeSpan.FromMinutes(4), vm.MarkOut);
        Assert.True(vm.IsMarked);
        Assert.True(vm.CanExportClip);
        Assert.Equal(20, vm.MarkInPercent, 1);
        Assert.Equal(80, vm.MarkOutPercent, 1);
    }

    [Fact]
    public void SetMarkOut_before_SetMarkIn_keeps_ordered_range_only()
    {
        var engine = new FakePlaybackEngine
        {
            LoadedFilePath = @"C:\media\video.mp4",
            Duration = TimeSpan.FromMinutes(5),
            IsSeekable = true
        };
        using var vm = new PlayerViewModel(engine);

        engine.Position = TimeSpan.FromMinutes(4);
        vm.SetMarkOut();
        Assert.Equal(TimeSpan.FromMinutes(4), vm.MarkOut);
        Assert.False(vm.IsMarked);

        // A later In (5 min) invalidates the earlier Out (4 min) → only the In is kept.
        engine.Position = TimeSpan.FromMinutes(5);
        vm.SetMarkIn();
        Assert.Equal(TimeSpan.FromMinutes(5), vm.MarkIn);
        Assert.Null(vm.MarkOut);
        Assert.False(vm.IsMarked);

        // A later Out (2 min) invalidates the earlier In (5 min) → only the Out is kept.
        engine.Position = TimeSpan.FromMinutes(2);
        vm.SetMarkOut();
        Assert.Null(vm.MarkIn);
        Assert.Equal(TimeSpan.FromMinutes(2), vm.MarkOut);
        Assert.False(vm.IsMarked);
    }

    [Fact]
    public void ClearMarks_clears_both_points()
    {
        var engine = new FakePlaybackEngine
        {
            LoadedFilePath = @"C:\media\video.mp4",
            Duration = TimeSpan.FromMinutes(5),
            IsSeekable = true
        };
        using var vm = new PlayerViewModel(engine);

        engine.Position = TimeSpan.FromMinutes(1);
        vm.SetMarkIn();
        engine.Position = TimeSpan.FromMinutes(4);
        vm.SetMarkOut();
        Assert.True(vm.IsMarked);

        vm.ClearMarks();
        Assert.Null(vm.MarkIn);
        Assert.Null(vm.MarkOut);
        Assert.False(vm.IsMarked);
    }

    [Fact]
    public void CanExportClip_is_false_for_remote_stream()
    {
        var engine = new FakePlaybackEngine
        {
            LoadedFilePath = "http://example.com/live/stream.m3u8",
            Duration = TimeSpan.FromMinutes(5),
            IsSeekable = true
        };
        using var vm = new PlayerViewModel(engine, clipExporter: new FakeClipExporter());

        engine.Position = TimeSpan.FromMinutes(1);
        vm.SetMarkIn();
        engine.Position = TimeSpan.FromMinutes(4);
        vm.SetMarkOut();

        Assert.True(vm.IsMarked);
        Assert.False(vm.CanExportClip);
    }

    [Fact]
    public void CanExportClip_is_false_without_exporter()
    {
        var engine = new FakePlaybackEngine
        {
            LoadedFilePath = @"C:\media\video.mp4",
            Duration = TimeSpan.FromMinutes(5),
            IsSeekable = true
        };
        using var vm = new PlayerViewModel(engine);

        engine.Position = TimeSpan.FromMinutes(1);
        vm.SetMarkIn();
        engine.Position = TimeSpan.FromMinutes(4);
        vm.SetMarkOut();

        Assert.False(vm.CanExportClip);
    }

    [Fact]
    public async Task ExportClipAsync_passes_path_start_and_duration_to_exporter()
    {
        var engine = new FakePlaybackEngine
        {
            LoadedFilePath = @"C:\media\video.mp4",
            Duration = TimeSpan.FromMinutes(5),
            IsSeekable = true
        };
        var exporter = new FakeClipExporter();
        using var vm = new PlayerViewModel(engine, clipExporter: exporter);

        engine.Position = TimeSpan.FromMinutes(1);
        vm.SetMarkIn();
        engine.Position = TimeSpan.FromMinutes(4);
        vm.SetMarkOut();

        var result = await vm.ExportClipAsync(@"C:\out\clip.mp4");

        Assert.True(result.Success);
        Assert.Equal(@"C:\media\video.mp4", exporter.LastInputPath);
        Assert.Equal(@"C:\out\clip.mp4", exporter.LastOutputPath);
        Assert.Equal(TimeSpan.FromMinutes(1), exporter.LastStart);
        Assert.Equal(TimeSpan.FromMinutes(3), exporter.LastDuration);
    }

    [Fact]
    public async Task ExportClipAsync_returns_failure_without_valid_marks()
    {
        var engine = new FakePlaybackEngine
        {
            LoadedFilePath = @"C:\media\video.mp4",
            Duration = TimeSpan.FromMinutes(5),
            IsSeekable = true
        };
        using var vm = new PlayerViewModel(engine, clipExporter: new FakeClipExporter());

        var result = await vm.ExportClipAsync(@"C:\out\clip.mp4");
        Assert.False(result.Success);
    }

    [Fact]
    public async Task ExportClipAsync_returns_failure_when_exporter_unavailable()
    {
        var engine = new FakePlaybackEngine
        {
            LoadedFilePath = @"C:\media\video.mp4",
            Duration = TimeSpan.FromMinutes(5),
            IsSeekable = true
        };
        using var vm = new PlayerViewModel(engine);

        engine.Position = TimeSpan.FromMinutes(1);
        vm.SetMarkIn();
        engine.Position = TimeSpan.FromMinutes(4);
        vm.SetMarkOut();

        var result = await vm.ExportClipAsync(@"C:\out\clip.mp4");
        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task LoadAsync_clears_marks_for_new_media()
    {
        var (mediaPath, dir) = CreateTempMedia();
        var engine = new FakePlaybackEngine
        {
            LoadedFilePath = mediaPath,
            Duration = TimeSpan.FromMinutes(5),
            IsSeekable = true
        };
        using var vm = new PlayerViewModel(engine);

        engine.Position = TimeSpan.FromMinutes(1);
        vm.SetMarkIn();
        engine.Position = TimeSpan.FromMinutes(4);
        vm.SetMarkOut();
        Assert.True(vm.IsMarked);

        await vm.LoadAsync(mediaPath);
        Assert.False(vm.IsMarked);
        Assert.Null(vm.MarkIn);
        Assert.Null(vm.MarkOut);

        Directory.Delete(dir, recursive: true);
    }

    private sealed class FakeMediaConverter : PlaySphere.Application.Media.IMediaConverter
    {
        public PlaySphere.Domain.Media.ConversionResult Result { get; set; } = PlaySphere.Domain.Media.ConversionResult.Ok;

        public string? LastInputPath { get; private set; }
        public string? LastOutputPath { get; private set; }
        public PlaySphere.Domain.Media.ConversionPreset? LastPreset { get; private set; }

        public Task<PlaySphere.Domain.Media.ConversionResult> ConvertAsync(
            string inputPath, string outputPath, PlaySphere.Domain.Media.ConversionPreset preset, CancellationToken cancellationToken = default, IProgress<TimeSpan>? progress = null)
        {
            LastInputPath = inputPath;
            LastOutputPath = outputPath;
            LastPreset = preset;
            return Task.FromResult(Result);
        }
    }

    // ── Phase 7 (PS-703): Media conversion ───────────────────────────────────

    [Fact]
    public void CanConvert_true_for_a_local_file_with_a_converter()
    {
        var engine = new FakePlaybackEngine { LoadedFilePath = @"C:\media\video.mp4" };
        using var vm = new PlayerViewModel(engine, mediaConverter: new FakeMediaConverter());

        Assert.True(vm.CanConvert);
        Assert.False(vm.IsConverting);
    }

    [Fact]
    public void CanConvert_false_without_a_converter()
    {
        var engine = new FakePlaybackEngine { LoadedFilePath = @"C:\media\video.mp4" };
        using var vm = new PlayerViewModel(engine);

        Assert.False(vm.CanConvert);
    }

    [Fact]
    public void CanConvert_false_for_a_remote_stream()
    {
        var engine = new FakePlaybackEngine { LoadedFilePath = "https://example.com/live/stream" };
        using var vm = new PlayerViewModel(engine, mediaConverter: new FakeMediaConverter());

        Assert.False(vm.CanConvert);
    }

    [Fact]
    public void CanConvert_false_for_no_loaded_file()
    {
        var engine = new FakePlaybackEngine();
        using var vm = new PlayerViewModel(engine, mediaConverter: new FakeMediaConverter());

        Assert.False(vm.CanConvert);
    }

    [Fact]
    public async Task ConvertAsync_passes_preset_and_paths_to_the_converter()
    {
        var (mediaPath, dir) = CreateTempMedia();
        var engine = new FakePlaybackEngine { LoadedFilePath = mediaPath };
        var converter = new FakeMediaConverter();
        using var vm = new PlayerViewModel(engine, mediaConverter: converter);

        var preset = PlaySphere.Domain.Media.ConversionPresets.Find("mp3-192")!;
        var result = await vm.ConvertAsync(preset, @"C:\out\song.mp3");

        Assert.True(result.Success);
        Assert.Equal(mediaPath, converter.LastInputPath);
        Assert.Equal(@"C:\out\song.mp3", converter.LastOutputPath);
        Assert.Equal(preset, converter.LastPreset);
        Assert.False(vm.IsConverting);

        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task ConvertAsync_returns_failure_when_converter_unavailable()
    {
        var (mediaPath, dir) = CreateTempMedia();
        var engine = new FakePlaybackEngine { LoadedFilePath = mediaPath };
        using var vm = new PlayerViewModel(engine);

        var result = await vm.ConvertAsync(PlaySphere.Domain.Media.ConversionPresets.All[0], @"C:\out\clip.mp4");

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);

        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task ConvertAsync_returns_failure_for_a_remote_stream()
    {
        var engine = new FakePlaybackEngine { LoadedFilePath = "https://example.com/live/stream" };
        using var vm = new PlayerViewModel(engine, mediaConverter: new FakeMediaConverter());

        var result = await vm.ConvertAsync(PlaySphere.Domain.Media.ConversionPresets.All[0], @"C:\out\clip.mp4");

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ConvertAsync_returns_failure_for_an_empty_output_path()
    {
        var (mediaPath, dir) = CreateTempMedia();
        var engine = new FakePlaybackEngine { LoadedFilePath = mediaPath };
        using var vm = new PlayerViewModel(engine, mediaConverter: new FakeMediaConverter());

        var result = await vm.ConvertAsync(PlaySphere.Domain.Media.ConversionPresets.All[0], "");

        Assert.False(result.Success);

        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task ConvertAsync_surfaces_converter_failure_and_clears_busy_state()
    {
        var (mediaPath, dir) = CreateTempMedia();
        var engine = new FakePlaybackEngine { LoadedFilePath = mediaPath };
        var converter = new FakeMediaConverter { Result = PlaySphere.Domain.Media.ConversionResult.Fail("nope") };
        using var vm = new PlayerViewModel(engine, mediaConverter: converter);

        var result = await vm.ConvertAsync(PlaySphere.Domain.Media.ConversionPresets.All[0], @"C:\out\clip.mp4");

        Assert.False(result.Success);
        Assert.Equal("nope", result.ErrorMessage);
        Assert.False(vm.IsConverting);

        Directory.Delete(dir, recursive: true);
    }

    // ── Phase 7 (PS-704): Timeline hover thumbnails ─────────────────────────

    private sealed class FakeThumbnailProvider : PlaySphere.Application.Media.IMediaThumbnailProvider
    {
        public PlaySphere.Domain.Media.ThumbnailStripResult Result { get; set; } =
            PlaySphere.Domain.Media.ThumbnailStripResult.Ok(@"C:\thumbs\strip.jpg", 4, 4, 160, 90);

        public int CallCount { get; private set; }
        public string? LastFilePath { get; private set; }
        public string? LastCacheDirectory { get; private set; }
        public TimeSpan LastDuration { get; private set; }
        public int LastFrameCount { get; private set; }

        public Task<PlaySphere.Domain.Media.ThumbnailStripResult> GenerateStripAsync(
            string filePath, string cacheDirectory, TimeSpan duration, int frameCount, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastFilePath = filePath;
            LastCacheDirectory = cacheDirectory;
            LastDuration = duration;
            LastFrameCount = frameCount;
            return Task.FromResult(Result);
        }
    }

    [Fact]
    public async Task PrepareThumbnailStripAsync_generates_a_strip_for_a_local_video()
    {
        var (mediaPath, dir) = CreateTempMedia();
        var engine = new FakePlaybackEngine
        {
            LoadedFilePath = mediaPath,
            Duration = TimeSpan.FromMinutes(2),
            Metadata = new MediaMetadata(
                TimeSpan.FromMinutes(2), null,
                [new VideoTrackInfo(0, null, null, "h264", null)],
                [], []),
        };
        var provider = new FakeThumbnailProvider();
        using var vm = new PlayerViewModel(engine, thumbnailProvider: provider, thumbnailStripCacheDir: @"C:\cache");

        await vm.PrepareThumbnailStripAsync();

        Assert.NotNull(vm.ThumbnailStrip);
        Assert.True(vm.ThumbnailStrip!.Success);
        Assert.Equal(1, provider.CallCount);
        Assert.Equal(mediaPath, provider.LastFilePath);
        Assert.Equal(@"C:\cache", provider.LastCacheDirectory);
        Assert.Equal(TimeSpan.FromMinutes(2), provider.LastDuration);
        Assert.InRange(provider.LastFrameCount, 1, 96);

        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task PrepareThumbnailStripAsync_skips_audio_only_media()
    {
        var (mediaPath, dir) = CreateTempMedia();
        var engine = new FakePlaybackEngine
        {
            LoadedFilePath = mediaPath,
            Duration = TimeSpan.FromMinutes(2),
            Metadata = new MediaMetadata(TimeSpan.FromMinutes(2), null, [], [], []),
        };
        var provider = new FakeThumbnailProvider();
        using var vm = new PlayerViewModel(engine, thumbnailProvider: provider);

        await vm.PrepareThumbnailStripAsync();

        Assert.Null(vm.ThumbnailStrip);
        Assert.Equal(0, provider.CallCount);

        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task PrepareThumbnailStripAsync_skips_remote_streams()
    {
        var engine = new FakePlaybackEngine
        {
            LoadedFilePath = "https://example.com/live/stream.m3u8",
            Duration = TimeSpan.FromMinutes(5),
            Metadata = new MediaMetadata(
                TimeSpan.FromMinutes(5), null,
                [new VideoTrackInfo(0, null, null, "h264", null)],
                [], []),
        };
        var provider = new FakeThumbnailProvider();
        using var vm = new PlayerViewModel(engine, thumbnailProvider: provider);

        await vm.PrepareThumbnailStripAsync();

        Assert.Null(vm.ThumbnailStrip);
        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public async Task PrepareThumbnailStripAsync_ignores_provider_failure()
    {
        var (mediaPath, dir) = CreateTempMedia();
        var engine = new FakePlaybackEngine
        {
            LoadedFilePath = mediaPath,
            Duration = TimeSpan.FromMinutes(2),
            Metadata = new MediaMetadata(
                TimeSpan.FromMinutes(2), null,
                [new VideoTrackInfo(0, null, null, "h264", null)],
                [], []),
        };
        var provider = new FakeThumbnailProvider
        {
            Result = PlaySphere.Domain.Media.ThumbnailStripResult.Fail("ffmpeg missing"),
        };
        using var vm = new PlayerViewModel(engine, thumbnailProvider: provider);

        await vm.PrepareThumbnailStripAsync();

        Assert.NotNull(vm.ThumbnailStrip);
        Assert.False(vm.ThumbnailStrip!.Success);

        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task GetThumbnailCellIndex_clamps_and_maps_fraction_to_grid()
    {
        var (mediaPath, dir) = CreateTempMedia();
        var engine = new FakePlaybackEngine
        {
            LoadedFilePath = mediaPath,
            Duration = TimeSpan.FromMinutes(2),
            Metadata = new MediaMetadata(
                TimeSpan.FromMinutes(2), null,
                [new VideoTrackInfo(0, null, null, "h264", null)],
                [], []),
        };
        var provider = new FakeThumbnailProvider
        {
            Result = PlaySphere.Domain.Media.ThumbnailStripResult.Ok(@"C:\thumbs\strip.jpg", 4, 4, 160, 90),
        };
        using var vm = new PlayerViewModel(engine, thumbnailProvider: provider);

        Assert.Equal(-1, vm.GetThumbnailCellIndex(0)); // no strip until prepared

        await vm.PrepareThumbnailStripAsync();
        Assert.Equal(0, vm.GetThumbnailCellIndex(0));
        Assert.Equal(8, vm.GetThumbnailCellIndex(50));
        Assert.Equal(15, vm.GetThumbnailCellIndex(99.9));
        Assert.Equal(0, vm.GetThumbnailCellIndex(-5));
        Assert.Equal(15, vm.GetThumbnailCellIndex(200));

        Directory.Delete(dir, recursive: true);
    }

    private static MediaMetadata MetadataWithAudio(params AudioTrackInfo[] audio) => new(
        TimeSpan.FromSeconds(300), null,
        [new VideoTrackInfo(0, null, null, "h264", null)],
        audio,
        []);

    // ── Phase 7 (PS-705): Timeline waveform ─────────────────────────────────

    private sealed class FakeWaveformProvider : PlaySphere.Application.Media.IWaveformProvider
    {
        public PlaySphere.Domain.Media.WaveformResult Result { get; set; } =
            PlaySphere.Domain.Media.WaveformResult.Ok(@"C:\waveforms\strip.png", 1080, 56);

        public int CallCount { get; private set; }
        public string? LastFilePath { get; private set; }
        public string? LastCacheDirectory { get; private set; }

        public Task<PlaySphere.Domain.Media.WaveformResult> GenerateAsync(string filePath, string cacheDirectory, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastFilePath = filePath;
            LastCacheDirectory = cacheDirectory;
            return Task.FromResult(Result);
        }
    }

    [Fact]
    public async Task PrepareWaveformAsync_generates_a_strip_for_a_local_video()
    {
        var (mediaPath, dir) = CreateTempMedia();
        var engine = new FakePlaybackEngine
        {
            LoadedFilePath = mediaPath,
            Duration = TimeSpan.FromMinutes(2),
            Metadata = MetadataWithAudio(new AudioTrackInfo(0, null, null, "aac", 48000, 2)),
        };
        var provider = new FakeWaveformProvider();
        using var vm = new PlayerViewModel(engine, waveformProvider: provider, waveformCacheDir: @"C:\wfcache");

        await vm.PrepareWaveformAsync();

        Assert.NotNull(vm.Waveform);
        Assert.True(vm.Waveform!.Success);
        Assert.Equal(1, provider.CallCount);
        Assert.Equal(mediaPath, provider.LastFilePath);
        Assert.Equal(@"C:\wfcache", provider.LastCacheDirectory);

        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task PrepareWaveformAsync_generates_a_strip_for_audio_only_media()
    {
        var (mediaPath, dir) = CreateTempMedia();
        var engine = new FakePlaybackEngine
        {
            LoadedFilePath = mediaPath,
            Duration = TimeSpan.FromMinutes(2),
            Metadata = new MediaMetadata(TimeSpan.FromMinutes(2), null, [],
                [new AudioTrackInfo(0, null, null, "aac", 48000, 2)], []),
        };
        var provider = new FakeWaveformProvider();
        using var vm = new PlayerViewModel(engine, waveformProvider: provider);

        await vm.PrepareWaveformAsync();

        Assert.NotNull(vm.Waveform);
        Assert.True(vm.Waveform!.Success);
        Assert.Equal(1, provider.CallCount);

        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task PrepareWaveformAsync_skips_media_without_an_audio_track()
    {
        var (mediaPath, dir) = CreateTempMedia();
        var engine = new FakePlaybackEngine
        {
            LoadedFilePath = mediaPath,
            Duration = TimeSpan.FromMinutes(2),
            Metadata = new MediaMetadata(TimeSpan.FromMinutes(2), null,
                [new VideoTrackInfo(0, null, null, "h264", null)], [], []),
        };
        var provider = new FakeWaveformProvider();
        using var vm = new PlayerViewModel(engine, waveformProvider: provider);

        await vm.PrepareWaveformAsync();

        Assert.Null(vm.Waveform);
        Assert.Equal(0, provider.CallCount);

        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task PrepareWaveformAsync_skips_remote_streams()
    {
        var engine = new FakePlaybackEngine
        {
            LoadedFilePath = "https://example.com/live/stream.m3u8",
            Duration = TimeSpan.FromMinutes(5),
            Metadata = MetadataWithAudio(new AudioTrackInfo(0, null, null, "aac", 48000, 2)),
        };
        var provider = new FakeWaveformProvider();
        using var vm = new PlayerViewModel(engine, waveformProvider: provider);

        await vm.PrepareWaveformAsync();

        Assert.Null(vm.Waveform);
        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public async Task PrepareWaveformAsync_ignores_provider_failure()
    {
        var (mediaPath, dir) = CreateTempMedia();
        var engine = new FakePlaybackEngine
        {
            LoadedFilePath = mediaPath,
            Duration = TimeSpan.FromMinutes(2),
            Metadata = MetadataWithAudio(new AudioTrackInfo(0, null, null, "aac", 48000, 2)),
        };
        var provider = new FakeWaveformProvider
        {
            Result = PlaySphere.Domain.Media.WaveformResult.Fail("ffmpeg missing"),
        };
        using var vm = new PlayerViewModel(engine, waveformProvider: provider);

        await vm.PrepareWaveformAsync();

        Assert.NotNull(vm.Waveform);
        Assert.False(vm.Waveform!.Success);

        Directory.Delete(dir, recursive: true);
    }

    private static MediaMetadata MetadataWithVideo() => new(
        TimeSpan.FromSeconds(300), null,
        [new VideoTrackInfo(0, "und", "Video", "h264", "1920x1080")],
        [new AudioTrackInfo(1, "und", null, "aac", 48000, 2)],
        []);

    // ── Phase 7 (PS-701) snapshot capture ───────────────────────────────────

    [Fact]
    public async Task SaveSnapshotAsync_captures_to_requested_path_when_video_loaded()
    {
        var engine = new FakePlaybackEngine
        {
            LoadedFilePath = @"C:\media\video.mp4",
            Metadata = MetadataWithVideo(),
            TakeSnapshotResult = true,
            WriteSnapshotFile = true
        };
        using var vm = new PlayerViewModel(engine);

        var outDir = Path.Combine(Path.GetTempPath(), "ps-snap-" + Guid.NewGuid().ToString("N"));
        var outPath = Path.Combine(outDir, "frame.png");

        try
        {
            Assert.True(vm.CanCaptureSnapshot);
            var ok = await vm.SaveSnapshotAsync(outPath);
            Assert.True(ok);
            Assert.Equal(outPath, engine.LastSnapshotOutputPath);
            Assert.True(File.Exists(outPath));
        }
        finally
        {
            if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);
        }
    }

    [Fact]
    public async Task SaveSnapshotAsync_writes_through_engine_result()
    {
        var engine = new FakePlaybackEngine
        {
            LoadedFilePath = @"C:\media\video.mp4",
            Metadata = MetadataWithVideo(),
            TakeSnapshotResult = false
        };
        using var vm = new PlayerViewModel(engine);

        var outDir = Path.Combine(Path.GetTempPath(), "ps-snap-" + Guid.NewGuid().ToString("N"));
        var outPath = Path.Combine(outDir, "frame.png");

        try
        {
            var ok = await vm.SaveSnapshotAsync(outPath);
            Assert.False(ok);
            Assert.Equal(outPath, engine.LastSnapshotOutputPath);
        }
        finally
        {
            if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);
        }
    }

    [Fact]
    public async Task SaveSnapshotAsync_returns_false_when_no_video_track()
    {
        var engine = new FakePlaybackEngine
        {
            LoadedFilePath = @"C:\media\audio.mp3",
            Metadata = MediaMetadata.Empty,
            TakeSnapshotResult = true
        };
        using var vm = new PlayerViewModel(engine);

        Assert.False(vm.CanCaptureSnapshot);
        var ok = await vm.SaveSnapshotAsync(@"C:\out\x.png");
        Assert.False(ok);
        Assert.Null(engine.LastSnapshotOutputPath);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SaveSnapshotAsync_returns_false_for_invalid_path(string? path)
    {
        var engine = new FakePlaybackEngine
        {
            LoadedFilePath = @"C:\media\video.mp4",
            Metadata = MetadataWithVideo()
        };
        using var vm = new PlayerViewModel(engine);

        var ok = await vm.SaveSnapshotAsync(path!);
        Assert.False(ok);
        Assert.Null(engine.LastSnapshotOutputPath);
    }

    [Fact]
    public async Task TracksChanged_auto_selects_preferred_audio_track()
    {
        var (mediaPath, dir) = CreateTempMedia();
        var engine = new FakePlaybackEngine();
        var prefs = new FakePreferencesStore { Languages = { "en" } };
        using var vm = new PlayerViewModel(engine, preferences: prefs);
        await vm.LoadAsync(mediaPath);
        engine.RaiseTracksChanged(MetadataWithAudio(
            new AudioTrackInfo(1, "fr", null, "mp4a", 48000, 2),
            new AudioTrackInfo(2, "en", null, "mp4a", 48000, 2)));

        Assert.Equal(2, engine.LastSelectedAudioTrackId);
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task TracksChanged_does_not_auto_select_when_no_track_matches()
    {
        var (mediaPath, dir) = CreateTempMedia();
        var engine = new FakePlaybackEngine();
        var prefs = new FakePreferencesStore { Languages = { "de" } };
        using var vm = new PlayerViewModel(engine, preferences: prefs);
        await vm.LoadAsync(mediaPath);
        engine.RaiseTracksChanged(MetadataWithAudio(
            new AudioTrackInfo(1, "fr", null, "mp4a", 48000, 2),
            new AudioTrackInfo(2, "en", null, "mp4a", 48000, 2)));

        Assert.Null(engine.LastSelectedAudioTrackId);
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task TracksChanged_does_not_auto_select_without_preferences()
    {
        var (mediaPath, dir) = CreateTempMedia();
        var engine = new FakePlaybackEngine();
        using var vm = new PlayerViewModel(engine);
        await vm.LoadAsync(mediaPath);
        engine.RaiseTracksChanged(MetadataWithAudio(
            new AudioTrackInfo(2, "en", null, "mp4a", 48000, 2)));

        Assert.Null(engine.LastSelectedAudioTrackId);
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task SelectAudioTrackAsync_learns_chosen_language_as_preference()
    {
        var (mediaPath, dir) = CreateTempMedia();
        var engine = new FakePlaybackEngine();
        var prefs = new FakePreferencesStore();
        using var vm = new PlayerViewModel(engine, preferences: prefs);
        await vm.LoadAsync(mediaPath);
        engine.RaiseTracksChanged(MetadataWithAudio(
            new AudioTrackInfo(1, "fr", null, "mp4a", 48000, 2),
            new AudioTrackInfo(2, "en-US", null, "mp4a", 48000, 2)));

        await vm.SelectAudioTrackAsync(2);

        Assert.Equal(2, engine.LastSelectedAudioTrackId);
        Assert.Single(prefs.Languages);
        Assert.Equal("en-US", prefs.Languages[0]);
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task LoadCollectionAsync_loads_first_file_and_activates_queue()
    {
        var (mediaPath, dir) = CreateTempMedia();
        var b = Path.Combine(dir, "b.mp4");
        var c = Path.Combine(dir, "c.mp4");
        File.WriteAllText(b, string.Empty);
        File.WriteAllText(c, string.Empty);
        var engine = new FakePlaybackEngine();
        using var vm = new PlayerViewModel(engine);

        await vm.LoadCollectionAsync([mediaPath, b, c]);

        Assert.True(vm.IsQueueActive);
        Assert.Equal(Path.GetFullPath(mediaPath), engine.LastLoadedMedia!.FilePath);
        Assert.Equal("1 / 3", vm.QueuePositionLabel);
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task LoadCollectionAsync_with_start_path_seeks_to_that_item()
    {
        var (mediaPath, dir) = CreateTempMedia();
        var b = Path.Combine(dir, "b.mp4");
        var c = Path.Combine(dir, "c.mp4");
        File.WriteAllText(b, string.Empty);
        File.WriteAllText(c, string.Empty);
        var engine = new FakePlaybackEngine();
        using var vm = new PlayerViewModel(engine);

        await vm.LoadCollectionAsync([mediaPath, b, c], startFilePath: b);

        Assert.Equal(Path.GetFullPath(b), engine.LastLoadedMedia!.FilePath);
        Assert.Equal("2 / 3", vm.QueuePositionLabel);
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task PlayNextAsync_walks_the_queue_then_returns_false_at_the_end()
    {
        var (a, dir) = CreateTempMedia();
        var b = Path.Combine(dir, "b.mp4");
        var c = Path.Combine(dir, "c.mp4");
        File.WriteAllText(b, string.Empty);
        File.WriteAllText(c, string.Empty);
        var engine = new FakePlaybackEngine();
        using var vm = new PlayerViewModel(engine);
        await vm.LoadCollectionAsync([a, b, c]);

        Assert.True(await vm.PlayNextAsync());
        Assert.Equal(Path.GetFullPath(b), engine.LastLoadedMedia!.FilePath);
        Assert.Equal("2 / 3", vm.QueuePositionLabel);

        Assert.True(await vm.PlayNextAsync());
        Assert.Equal(Path.GetFullPath(c), engine.LastLoadedMedia!.FilePath);
        Assert.Equal("3 / 3", vm.QueuePositionLabel);

        Assert.False(await vm.PlayNextAsync());
        Assert.Equal(Path.GetFullPath(c), engine.LastLoadedMedia!.FilePath);
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task PlayNextAsync_with_repeat_all_wraps_to_first_item()
    {
        var (a, dir) = CreateTempMedia();
        var b = Path.Combine(dir, "b.mp4");
        File.WriteAllText(b, string.Empty);
        var engine = new FakePlaybackEngine();
        using var vm = new PlayerViewModel(engine);
        await vm.LoadCollectionAsync([a, b]);
        await vm.PlayNextAsync(); // now b (last)

        vm.CycleRepeatAsync(); // Off -> One
        vm.CycleRepeatAsync(); // One -> All

        Assert.True(await vm.PlayNextAsync());
        Assert.Equal(Path.GetFullPath(a), engine.LastLoadedMedia!.FilePath);
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task PlayPreviousAsync_wraps_from_first_to_last()
    {
        var (a, dir) = CreateTempMedia();
        var b = Path.Combine(dir, "b.mp4");
        var c = Path.Combine(dir, "c.mp4");
        File.WriteAllText(b, string.Empty);
        File.WriteAllText(c, string.Empty);
        var engine = new FakePlaybackEngine();
        using var vm = new PlayerViewModel(engine);
        await vm.LoadCollectionAsync([a, b, c]);

        Assert.True(await vm.PlayPreviousAsync());
        Assert.Equal(Path.GetFullPath(c), engine.LastLoadedMedia!.FilePath);
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task ToggleShuffleAsync_keeps_current_and_toggles_state()
    {
        var (a, dir) = CreateTempMedia();
        var b = Path.Combine(dir, "b.mp4");
        var c = Path.Combine(dir, "c.mp4");
        File.WriteAllText(b, string.Empty);
        File.WriteAllText(c, string.Empty);
        var engine = new FakePlaybackEngine();
        using var vm = new PlayerViewModel(engine);
        await vm.LoadCollectionAsync([a, b, c]);

        vm.ToggleShuffleAsync();
        Assert.True(vm.IsShuffled);
        Assert.Equal(Path.GetFullPath(a), engine.LastLoadedMedia!.FilePath); // current unchanged

        vm.ToggleShuffleAsync();
        Assert.False(vm.IsShuffled);
        Assert.Equal(Path.GetFullPath(a), engine.LastLoadedMedia!.FilePath);
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task CycleRepeatAsync_cycles_off_one_all()
    {
        var (mediaPath, dir) = CreateTempMedia();
        var engine = new FakePlaybackEngine();
        using var vm = new PlayerViewModel(engine);

        vm.CycleRepeatAsync();
        Assert.Equal("Repeat One", vm.RepeatModeLabel);
        vm.CycleRepeatAsync();
        Assert.Equal("Repeat All", vm.RepeatModeLabel);
        vm.CycleRepeatAsync();
        Assert.Equal("Repeat Off", vm.RepeatModeLabel);
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public async Task EndReached_auto_advances_to_next_queued_item()
    {
        var (a, dir) = CreateTempMedia();
        var b = Path.Combine(dir, "b.mp4");
        File.WriteAllText(b, string.Empty);
        var engine = new FakePlaybackEngine();
        using var vm = new PlayerViewModel(engine);
        await vm.LoadCollectionAsync([a, b]);

        engine.RaiseEndReached();

        Assert.Equal(Path.GetFullPath(b), engine.LastLoadedMedia!.FilePath);
        Assert.Equal("2 / 2", vm.QueuePositionLabel);
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void IsSeekable_reflects_engine_so_live_streams_are_not_seekable()
    {
        var seekable = new FakePlaybackEngine { Duration = TimeSpan.FromMinutes(3) };
        using (var vm = new PlayerViewModel(seekable))
        {
            Assert.True(vm.IsSeekable);
        }

        var live = new FakePlaybackEngine { IsSeekable = false, Duration = TimeSpan.Zero };
        using (var vm = new PlayerViewModel(live))
        {
            Assert.False(vm.IsSeekable);
        }
    }

    [Fact]
    public async Task SkipForwardAsync_on_non_seekable_stream_is_no_op()
    {
        var engine = new FakePlaybackEngine
        {
            IsSeekable = false,
            Duration = TimeSpan.Zero,
            Position = TimeSpan.FromSeconds(20)
        };
        using var vm = new PlayerViewModel(engine);

        await vm.SkipForwardAsync();
        Assert.Null(engine.LastSeekTarget);
    }

    [Fact]
    public async Task SkipBackAsync_on_non_seekable_stream_is_no_op()
    {
        var engine = new FakePlaybackEngine
        {
            IsSeekable = false,
            Duration = TimeSpan.Zero,
            Position = TimeSpan.FromSeconds(20)
        };
        using var vm = new PlayerViewModel(engine);

        await vm.SkipBackAsync();
        Assert.Null(engine.LastSeekTarget);
    }

    [Fact]
    public async Task SeekToPercentAsync_on_non_seekable_stream_is_no_op()
    {
        var engine = new FakePlaybackEngine
        {
            IsSeekable = false,
            Duration = TimeSpan.Zero
        };
        using var vm = new PlayerViewModel(engine);

        await vm.SeekToPercentAsync(50.0);
        Assert.Null(engine.LastSeekTarget);
    }

    [Fact]
    public async Task Chapters_CanGenerateChapters_false_without_cataloged_item()
    {
        var repo = new FakeLibraryRepository();
        var engine = new FakePlaybackEngine();
        var (mediaPath, dir) = CreateTempMedia();
        try
        {
            using var vm = new PlayerViewModel(engine, repo, generateChaptersRunner: new FakeChaptersRunner());
            await vm.LoadAsync(mediaPath);
            Assert.False(vm.CanGenerateChapters);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Chapters_CanGenerateChapters_false_without_runner()
    {
        var (repo, mediaPath, engine) = CreateCtx(TimeSpan.Zero, 0);
        using var vm = new PlayerViewModel(engine, repo);
        await vm.LoadAsync(mediaPath);
        Assert.False(vm.CanGenerateChapters);
    }

    [Fact]
    public async Task Chapters_generate_success_sets_chapters_and_message()
    {
        var (repo, mediaPath, engine) = CreateCtx(TimeSpan.Zero, 0);
        var runner = new FakeChaptersRunner
        {
            Result = new GenerateChaptersResult(true, null, new[]
            {
                new AiChapterEntity { Title = "Intro", StartSeconds = 0 },
                new AiChapterEntity { Title = "Main", StartSeconds = 5 },
            }),
        };
        using var vm = new PlayerViewModel(engine, repo, generateChaptersRunner: runner);
        await vm.LoadAsync(mediaPath);

        Assert.True(vm.CanGenerateChapters);
        var ok = await vm.GenerateChaptersAsync();

        Assert.True(ok);
        Assert.Equal(1, runner.CallCount);
        Assert.Equal(2, vm.Chapters.Count);
        Assert.Equal("Intro", vm.Chapters[0].Title);
        Assert.Contains("Generated 2 chapters", vm.ChaptersMessage);
        Assert.False(vm.IsGeneratingChapters);
    }

    [Fact]
    public async Task Chapters_generate_failure_sets_error_message()
    {
        var (repo, mediaPath, engine) = CreateCtx(TimeSpan.Zero, 0);
        var runner = new FakeChaptersRunner
        {
            Result = GenerateChaptersResult.Fail("No API key."),
        };
        using var vm = new PlayerViewModel(engine, repo, generateChaptersRunner: runner);
        await vm.LoadAsync(mediaPath);

        var ok = await vm.GenerateChaptersAsync();

        Assert.False(ok);
        Assert.Contains("No API key", vm.ChaptersMessage);
        Assert.Empty(vm.Chapters);
    }

    [Fact]
    public async Task Chapters_load_loads_persisted_chapters()
    {
        var (repo, mediaPath, engine) = CreateCtx(TimeSpan.Zero, 0);
        await repo.SaveChaptersAsync(7, new[]
        {
            new AiChapterEntity { MediaItemId = 7, Title = "Main", StartSeconds = 5 },
            new AiChapterEntity { MediaItemId = 7, Title = "Intro", StartSeconds = 0 },
        });
        using var vm = new PlayerViewModel(engine, repo);
        await vm.LoadAsync(mediaPath);

        await vm.LoadChaptersAsync();

        Assert.Collection(vm.Chapters,
            c => Assert.Equal(0, c.StartSeconds),
            c => Assert.Equal(5, c.StartSeconds));
    }

    [Fact]
    public async Task Chapters_seek_to_chapter_seeks_clamped_target()
    {
        var (repo, mediaPath, engine) = CreateCtx(TimeSpan.Zero, 0);
        engine.Duration = TimeSpan.FromSeconds(300);
        using var vm = new PlayerViewModel(engine, repo);
        await vm.LoadAsync(mediaPath);

        await vm.SeekToChapterAsync(new AiChapterEntity { Title = "Main", StartSeconds = 42 });

        Assert.Equal(TimeSpan.FromSeconds(42), engine.LastSeekTarget);
    }

    [Fact]
    public async Task Chapters_seek_to_chapter_clamps_at_duration()
    {
        var (repo, mediaPath, engine) = CreateCtx(TimeSpan.Zero, 0);
        engine.Duration = TimeSpan.FromSeconds(60);
        using var vm = new PlayerViewModel(engine, repo);
        await vm.LoadAsync(mediaPath);

        await vm.SeekToChapterAsync(new AiChapterEntity { Title = "Far", StartSeconds = 500 });

        Assert.Equal(TimeSpan.FromSeconds(60), engine.LastSeekTarget);
    }

    [Fact]
    public async Task Chapters_cleared_on_new_load()
    {
        var (repo, mediaPath, engine) = CreateCtx(TimeSpan.Zero, 0);
        var runner = new FakeChaptersRunner
        {
            Result = new GenerateChaptersResult(true, null, new[]
            {
                new AiChapterEntity { Title = "Intro", StartSeconds = 0 },
            }),
        };
        using var vm = new PlayerViewModel(engine, repo, generateChaptersRunner: runner);
        await vm.LoadAsync(mediaPath);
        await vm.GenerateChaptersAsync();
        Assert.Equal(1, vm.Chapters.Count);

        await vm.LoadAsync(mediaPath);
        Assert.Empty(vm.Chapters);
    }

    private sealed class FakeChaptersRunner : IGenerateChaptersRunner
    {
        public GenerateChaptersResult Result { get; set; } =
            new(true, null, new[] { new AiChapterEntity { Title = "Intro", StartSeconds = 0 } });
        public int CallCount { get; private set; }

        public Task<GenerateChaptersResult> GenerateChaptersAsync(string mediaFilePath, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(Result);
        }
    }

    private static (FakeLibraryRepository repo, string mediaPath, FakePlaybackEngine engine) CreateCtx(
        TimeSpan resumePosition, double watchedPercentage)
    {
        var (mediaPath, dir) = CreateTempMedia();
        var repo = new FakeLibraryRepository();
        repo.Add(new LibraryMediaItem
        {
            Id = 7,
            FilePath = mediaPath,
            Fingerprint = "hash",
            LastPosition = resumePosition,
            WatchedPercentage = watchedPercentage,
            Duration = TimeSpan.FromSeconds(300),
        });
        var engine = new FakePlaybackEngine();
        return (repo, mediaPath, engine);
    }

    private static (string mediaPath, string dir) CreateTempMedia()
    {
        var dir = Path.Combine(Path.GetTempPath(), "PlaySphereViewModelSession_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var mediaPath = Path.Combine(dir, "Movie.mp4");
        File.WriteAllText(mediaPath, string.Empty);
        return (mediaPath, dir);
    }
}
