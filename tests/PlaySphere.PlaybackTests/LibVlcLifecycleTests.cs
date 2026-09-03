using Microsoft.Extensions.Logging.Abstractions;
using PlaySphere.Playback.Abstractions;
using PlaySphere.Playback.LibVLC;
using Xunit;
using LibVLCLib = LibVLCSharp.Shared.LibVLC;

namespace PlaySphere.PlaybackTests;

/// <summary>
/// One real native libVLC instance shared by all lifecycle tests (mirrors the DI
/// singleton registration) so the expensive native bootstrap runs once per class.
/// </summary>
public sealed class LibVlcFixture : IDisposable
{
    public LibVLCLib LibVlc { get; } =
        LibVlcBootstrap.CreateLibVlc(new LibVlcOptions { DisableAudio = true });

    public LibVlcPlaybackEngine CreateEngine() =>
        new(GetInstance(), NullLogger<LibVlcPlaybackEngine>.Instance);

    private LibVLCLib GetInstance() => LibVlc;

    public void Dispose() => LibVlc.Dispose();
}

[CollectionDefinition(Name)]
public sealed class LibVlcCollection : ICollectionFixture<LibVlcFixture>
{
    public const string Name = "libvlc";
}

/// <summary>
/// Lifecycle smoke tests for the PS-102 libVLC adapter skeleton. These load the real
/// native libVLC and must keep the process 64-bit.
/// </summary>
[Collection(LibVlcCollection.Name)]
public sealed class LibVlcLifecycleTests
{
    private readonly LibVlcFixture _fixture;

    public LibVlcLifecycleTests(LibVlcFixture fixture) => _fixture = fixture;

    [Fact]
    public void Native_libvlc_instance_creates_with_version()
    {
        Assert.False(string.IsNullOrWhiteSpace(_fixture.LibVlc.Version));
        Assert.Matches("^\\d+\\.\\d+\\.\\d+", _fixture.LibVlc.Version);
    }

    [Fact]
    public void Engine_starts_in_Empty_state()
    {
        using var engine = _fixture.CreateEngine();

        Assert.Equal(PlaybackState.Empty, engine.State);
        Assert.Equal(TimeSpan.Zero, engine.Position);
        Assert.Equal(TimeSpan.Zero, engine.Duration);
        Assert.False(engine.IsMuted);
        Assert.True(engine.Volume is >= 0 and <= 100);
        Assert.Equal(1.0, engine.Rate);
        Assert.Empty(engine.Metadata.AudioTracks);
    }

    [Fact]
    public void Engine_implements_the_playback_contract()
    {
        using IMediaPlaybackEngine engine = _fixture.CreateEngine();

        Assert.IsAssignableFrom<IMediaPlaybackEngine>(engine);
        // Contract surface coverage: events are exposed and subscribeable before any media.
        engine.StateChanged += (_, _) => { };
        engine.PositionChanged += (_, _) => { };
        engine.EndReached += (_, _) => { };
        engine.ErrorOccurred += (_, _) => { };
        engine.TracksChanged += (_, _) => { };
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        var engine = _fixture.CreateEngine();

        engine.Dispose();
        engine.Dispose(); // must not throw
    }

    [Fact]
    public void CreateLibVlc_rejects_null_options()
    {
        Assert.Throws<ArgumentNullException>(() => LibVlcBootstrap.CreateLibVlc(null!));
    }
}