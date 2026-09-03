using PlaySphere.Playback.Abstractions;
using PlaySphere.Domain.Media;
using Xunit;

namespace PlaySphere.UnitTests;

/// <summary>Contract-level tests for PICKLE.md §15/§16 (PS-101). Pure logic — no engine.</summary>
public sealed class PlaybackContractTests
{
    [Theory]
    [InlineData("Empty")]
    [InlineData("Loading")]
    [InlineData("Ready")]
    [InlineData("Playing")]
    [InlineData("Paused")]
    [InlineData("Stopped")]
    [InlineData("Error")]
    public void PlaybackState_exposes_expected_member(string name)
    {
        Assert.True(Enum.IsDefined(typeof(PlaybackState), name), $"PlaybackState.{name} is missing.");
    }

    [Fact]
    public void PlaybackState_starts_enumeration_at_Empty()
    {
        Assert.Equal(PlaybackState.Empty, (PlaybackState)0);
    }

    [Fact]
    public void ClampRate_honors_bounds()
    {
        Assert.Equal(PlaybackConstraints.MinRate, PlaybackConstraints.ClampRate(0.0));
        Assert.Equal(PlaybackConstraints.MinRate, PlaybackConstraints.ClampRate(0.25));
        Assert.Equal(2.0, PlaybackConstraints.ClampRate(2.0));
        Assert.Equal(PlaybackConstraints.MaxRate, PlaybackConstraints.ClampRate(8.0));
    }

    [Fact]
    public void ClampVolume_honors_bounds()
    {
        Assert.Equal(0, PlaybackConstraints.ClampVolume(-10));
        Assert.Equal(100, PlaybackConstraints.ClampVolume(200));
        Assert.Equal(75, PlaybackConstraints.ClampVolume(75));
    }

    [Fact]
    public void ClampSubtitleDelay_honors_bounds()
    {
        Assert.Equal(PlaybackConstraints.MinSubtitleDelay, PlaybackConstraints.ClampSubtitleDelay(TimeSpan.FromSeconds(-120)));
        Assert.Equal(PlaybackConstraints.MaxSubtitleDelay, PlaybackConstraints.ClampSubtitleDelay(TimeSpan.FromSeconds(120)));
        Assert.Equal(TimeSpan.FromMilliseconds(250), PlaybackConstraints.ClampSubtitleDelay(TimeSpan.FromMilliseconds(250)));
        Assert.Equal(TimeSpan.FromMilliseconds(-500), PlaybackConstraints.ClampSubtitleDelay(TimeSpan.FromMilliseconds(-500)));
    }

    [Fact]
    public void ClampAudioDelay_honors_bounds()
    {
        Assert.Equal(PlaybackConstraints.MinAudioDelay, PlaybackConstraints.ClampAudioDelay(TimeSpan.FromSeconds(-60)));
        Assert.Equal(PlaybackConstraints.MaxAudioDelay, PlaybackConstraints.ClampAudioDelay(TimeSpan.FromSeconds(60)));
        Assert.Equal(TimeSpan.FromMilliseconds(300), PlaybackConstraints.ClampAudioDelay(TimeSpan.FromMilliseconds(300)));
    }

    [Fact]
    public void MediaFile_FromPath_rejects_blank_paths()
    {
        Assert.Throws<ArgumentException>(() => MediaFile.FromPath(""));
        Assert.Throws<ArgumentException>(() => MediaFile.FromPath("   "));
        Assert.Throws<ArgumentException>(() => MediaFile.FromPath(null!));
    }

    [Fact]
    public void MediaFile_FromPath_canonicalizes_and_sets_display_title()
    {
        var media = MediaFile.FromPath(@"C:\Media\Some Movie.mp4");
        Assert.Equal(Path.GetFullPath(@"C:\Media\Some Movie.mp4"), media.FilePath);
        Assert.Equal("Some Movie", media.DisplayTitle);
        Assert.False(media.IsRemoteUri);
    }

    [Fact]
    public void MediaFile_FromUri_marks_remote_and_derives_title_from_path_segment()
    {
        var media = MediaFile.FromUri("https://cdn.example.com/live/concert.ts");
        Assert.True(media.IsRemoteUri);
        Assert.NotNull(media.SourceUri);
        Assert.Equal("https://cdn.example.com/live/concert.ts", media.FilePath);
        Assert.Equal("concert", media.DisplayTitle);
    }

    [Fact]
    public void MediaFile_FromUri_falls_back_to_host_when_no_meaningful_segment()
    {
        var media = MediaFile.FromUri("https://radio.example.com/");
        Assert.Equal("radio.example.com", media.DisplayTitle);
    }

    [Fact]
    public void MediaFile_FromUri_accepts_common_stream_schemes()
    {
        Assert.True(MediaFile.FromUri("https://x/y.m3u8").IsRemoteUri);
        Assert.True(MediaFile.FromUri("rtsp://server/live").IsRemoteUri);
        Assert.True(MediaFile.FromUri("rtmp://server/live").IsRemoteUri);
        Assert.True(MediaFile.FromUri("mms://server/stream").IsRemoteUri);
    }

    [Fact]
    public void MediaFile_FromUri_rejects_blank_and_unsupported_schemes()
    {
        Assert.Throws<ArgumentException>(() => MediaFile.FromUri(""));
        Assert.Throws<ArgumentException>(() => MediaFile.FromUri("   "));
        Assert.Throws<ArgumentException>(() => MediaFile.FromUri("nops://not-a-real-scheme/x"));
    }

    [Fact]
    public void MediaFile_FromPathOrUri_dispatches_local_paths_and_remote_uris()
    {
        Assert.False(MediaFile.FromPathOrUri(@"C:\Media\movie.mp4").IsRemoteUri);
        Assert.True(MediaFile.FromPathOrUri("https://example.com/live/stream.mp4").IsRemoteUri);
        Assert.Equal("stream", MediaFile.FromPathOrUri("https://example.com/live/stream.mp4").DisplayTitle);
    }

    [Fact]
    public void MediaMetadata_Empty_is_well_formed()
    {
        var m = MediaMetadata.Empty;
        Assert.Equal(TimeSpan.Zero, m.Duration);
        Assert.Empty(m.VideoTracks);
        Assert.Empty(m.AudioTracks);
        Assert.Empty(m.SubtitleTracks);
    }

    [Fact]
    public void PlaybackStateChangedEventArgs_carries_transition()
    {
        var args = new PlaybackStateChangedEventArgs(PlaybackState.Ready, PlaybackState.Playing);
        Assert.Equal(PlaybackState.Ready, args.Previous);
        Assert.Equal(PlaybackState.Playing, args.Current);
    }

    [Fact]
    public void PlaybackErrorEventArgs_carries_message_and_exception()
    {
        var ex = new InvalidOperationException("boom");
        var args = new PlaybackErrorEventArgs("load failed", ex);
        Assert.Equal("load failed", args.Message);
        Assert.Same(ex, args.Exception);
    }

    [Fact]
    public void PlaybackPositionChangedEventArgs_carries_position()
    {
        var args = new PlaybackPositionChangedEventArgs(TimeSpan.FromSeconds(90));
        Assert.Equal(TimeSpan.FromSeconds(90), args.Position);
    }
}