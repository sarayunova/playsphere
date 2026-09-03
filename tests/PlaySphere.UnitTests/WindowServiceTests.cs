using PlaySphere.UI.Services;
using Xunit;

namespace PlaySphere.UnitTests;

public sealed class WindowServiceTests
{
    [Fact]
    public void Initial_state_is_windowed_and_not_cinema_and_not_compact()
    {
        var service = new AppWindowService(null);

        Assert.False(service.IsFullScreen);
        Assert.False(service.IsCinemaMode);
        Assert.False(service.IsCompactOverlay);
    }

    [Fact]
    public void ToggleFullScreen_toggles_and_raises_event()
    {
        var service = new AppWindowService(null);
        var eventCount = 0;
        service.FullScreenChanged += (_, _) => eventCount++;

        service.ToggleFullScreen();
        Assert.True(service.IsFullScreen);
        Assert.Equal(1, eventCount);

        service.ToggleFullScreen();
        Assert.False(service.IsFullScreen);
        Assert.Equal(2, eventCount);
    }

    [Fact]
    public void SetFullScreen_no_op_when_same_value()
    {
        var service = new AppWindowService(null);
        var eventCount = 0;
        service.FullScreenChanged += (_, _) => eventCount++;

        service.SetFullScreen(false);
        Assert.False(service.IsFullScreen);
        Assert.Equal(0, eventCount);

        service.SetFullScreen(true);
        Assert.True(service.IsFullScreen);
        Assert.Equal(1, eventCount);

        service.SetFullScreen(true);
        Assert.True(service.IsFullScreen);
        Assert.Equal(1, eventCount);
    }

    [Fact]
    public void ToggleCinemaMode_toggles_and_raises_event()
    {
        var service = new AppWindowService(null);
        var eventCount = 0;
        service.CinemaModeChanged += (_, _) => eventCount++;

        service.ToggleCinemaMode();
        Assert.True(service.IsCinemaMode);
        Assert.Equal(1, eventCount);

        service.ToggleCinemaMode();
        Assert.False(service.IsCinemaMode);
        Assert.Equal(2, eventCount);
    }

    [Fact]
    public void SetCinemaMode_no_op_when_same_value()
    {
        var service = new AppWindowService(null);
        var eventCount = 0;
        service.CinemaModeChanged += (_, _) => eventCount++;

        service.SetCinemaMode(false);
        Assert.False(service.IsCinemaMode);
        Assert.Equal(0, eventCount);

        service.SetCinemaMode(true);
        Assert.True(service.IsCinemaMode);
        Assert.Equal(1, eventCount);

        service.SetCinemaMode(true);
        Assert.True(service.IsCinemaMode);
        Assert.Equal(1, eventCount);
    }

    [Fact]
    public void ToggleCompactOverlay_toggles_and_raises_event()
    {
        var service = new AppWindowService(null);
        var eventCount = 0;
        service.CompactOverlayChanged += (_, _) => eventCount++;

        service.ToggleCompactOverlay();
        Assert.True(service.IsCompactOverlay);
        Assert.Equal(1, eventCount);

        service.ToggleCompactOverlay();
        Assert.False(service.IsCompactOverlay);
        Assert.Equal(2, eventCount);
    }

    [Fact]
    public void SetCompactOverlay_no_op_when_same_value()
    {
        var service = new AppWindowService(null);
        var eventCount = 0;
        service.CompactOverlayChanged += (_, _) => eventCount++;

        service.SetCompactOverlay(false);
        Assert.False(service.IsCompactOverlay);
        Assert.Equal(0, eventCount);

        service.SetCompactOverlay(true);
        Assert.True(service.IsCompactOverlay);
        Assert.Equal(1, eventCount);

        service.SetCompactOverlay(true);
        Assert.True(service.IsCompactOverlay);
        Assert.Equal(1, eventCount);
    }

    [Fact]
    public void FullScreen_and_CompactOverlay_are_mutually_exclusive()
    {
        var service = new AppWindowService(null);

        service.SetFullScreen(true);
        Assert.True(service.IsFullScreen);
        Assert.False(service.IsCompactOverlay);

        service.SetCompactOverlay(true);
        Assert.True(service.IsCompactOverlay);
        // CompactOverlay disables FullScreen
        service.SetFullScreen(true);
        Assert.True(service.IsFullScreen);
    }
}
