using Golether.UI.Controls;

namespace Golether.UI.Tests;

/// <summary>
/// Tests of <see cref="ChromeAutoHide"/>: when the bars and the cursor of the full-screen window are shown.
/// </summary>
public sealed class ChromeAutoHideTests
{
    /// <summary>
    /// The delay used by the tests.
    /// </summary>
    private static readonly TimeSpan Delay = TimeSpan.FromSeconds(3);

    /// <summary>
    /// The clock moved by the tests.
    /// </summary>
    private readonly TestTime _time = new();

    /// <summary>
    /// Builds the object under test, already in full screen.
    /// </summary>
    /// <returns>The object.</returns>
    private ChromeAutoHide CreateActive()
    {
        var chrome = new ChromeAutoHide(_time, Delay);
        chrome.SetCursorHiding(true);
        chrome.SetBarsHiding(true);
        return chrome;
    }

    /// <summary>
    /// Moves the time forward and lets the object notice.
    /// </summary>
    /// <param name="chrome">The object.</param>
    /// <param name="seconds">The step in seconds.</param>
    private void Wait(ChromeAutoHide chrome, double seconds)
    {
        _time.Advance(TimeSpan.FromSeconds(seconds));
        chrome.Tick();
    }

    /// <summary>
    /// Outside full screen nothing is ever hidden, however long the pointer rests on the video.
    /// </summary>
    [Fact]
    public void OutsideFullScreen_EverythingStays()
    {
        var chrome = new ChromeAutoHide(_time, Delay);
        chrome.PointerMoved(PointerZone.Video);
        Wait(chrome, 60);

        Assert.True(chrome.BarsVisible);
        Assert.True(chrome.CursorVisible);
    }

    /// <summary>
    /// In a window that is not in full screen the cursor still disappears over the video, and the bars stay.
    /// </summary>
    [Fact]
    public void CursorHidingAlone_LeavesTheBars()
    {
        var chrome = new ChromeAutoHide(_time, Delay);
        chrome.SetCursorHiding(true);
        chrome.PointerMoved(PointerZone.Video);
        Wait(chrome, 3.1);

        Assert.False(chrome.CursorVisible);
        Assert.True(chrome.BarsVisible);

        chrome.PointerMoved(PointerZone.Elsewhere);
        Wait(chrome, 60);
        Assert.True(chrome.CursorVisible);
        Assert.True(chrome.BarsVisible);
    }

    /// <summary>
    /// Entering full screen shows the bars and takes them away after the delay.
    /// </summary>
    [Fact]
    public void EnteringFullScreen_HidesTheBarsAfterTheDelay()
    {
        var chrome = CreateActive();
        Assert.True(chrome.BarsVisible);

        Wait(chrome, 2.9);
        Assert.True(chrome.BarsVisible);

        Wait(chrome, 0.2);
        Assert.False(chrome.BarsVisible);
    }

    /// <summary>
    /// The cursor disappears after it rested over the video, and any movement brings it back.
    /// </summary>
    [Fact]
    public void Cursor_HidesOverTheVideoAndReturnsOnMovement()
    {
        var chrome = CreateActive();
        chrome.PointerMoved(PointerZone.Video);
        Wait(chrome, 2);
        Assert.True(chrome.CursorVisible);

        // A movement starts the wait again.
        chrome.PointerMoved(PointerZone.Video);
        Wait(chrome, 2);
        Assert.True(chrome.CursorVisible);

        Wait(chrome, 1.1);
        Assert.False(chrome.CursorVisible);

        chrome.PointerMoved(PointerZone.Video);
        Assert.True(chrome.CursorVisible);
    }

    /// <summary>
    /// Over the participants and the chat the cursor never disappears.
    /// </summary>
    [Fact]
    public void Cursor_StaysOutsideTheVideo()
    {
        var chrome = CreateActive();
        chrome.PointerMoved(PointerZone.Elsewhere);
        Wait(chrome, 60);

        Assert.True(chrome.CursorVisible);
        Assert.False(chrome.BarsVisible);
    }

    /// <summary>
    /// A movement over the video does not bring hidden bars back; the edge does, and they stay while the pointer is
    /// on them.
    /// </summary>
    [Fact]
    public void Bars_ReturnOnlyFromTheirZoneAndStayUnderThePointer()
    {
        var chrome = CreateActive();
        Wait(chrome, 6);
        Assert.False(chrome.BarsVisible);

        chrome.PointerMoved(PointerZone.Video);
        Assert.False(chrome.BarsVisible);

        chrome.PointerMoved(PointerZone.Bars);
        Assert.True(chrome.BarsVisible);
        Wait(chrome, 60);
        Assert.True(chrome.BarsVisible);
        Assert.True(chrome.CursorVisible);
    }

    /// <summary>
    /// After the pointer left the bars they stay for the delay, counted from the moment of leaving.
    /// </summary>
    [Fact]
    public void Bars_LeaveAfterThePointerLeft()
    {
        var chrome = CreateActive();
        chrome.PointerMoved(PointerZone.Bars);
        Wait(chrome, 30);

        chrome.PointerMoved(PointerZone.Video);
        Wait(chrome, 2.9);
        Assert.True(chrome.BarsVisible);

        // Movements over the video do not keep the bars.
        chrome.PointerMoved(PointerZone.Video);
        Wait(chrome, 0.2);
        Assert.False(chrome.BarsVisible);
        Assert.True(chrome.CursorVisible);
    }

    /// <summary>
    /// An open menu or a drag keeps everything shown; letting go starts the wait anew.
    /// </summary>
    [Fact]
    public void Hold_KeepsEverythingAndRestartsTheWait()
    {
        var chrome = CreateActive();
        chrome.PointerMoved(PointerZone.Video);
        chrome.SetHeld(true);
        Wait(chrome, 60);
        Assert.True(chrome.BarsVisible);
        Assert.True(chrome.CursorVisible);

        chrome.SetHeld(false);
        Wait(chrome, 2.9);
        Assert.True(chrome.BarsVisible);
        Assert.True(chrome.CursorVisible);

        Wait(chrome, 0.2);
        Assert.False(chrome.BarsVisible);
        Assert.False(chrome.CursorVisible);
    }

    /// <summary>
    /// Leaving full screen shows everything at once, and every change is reported once.
    /// </summary>
    [Fact]
    public void LeavingFullScreen_ShowsEverything_AndChangesAreReported()
    {
        var chrome = CreateActive();
        var changes = 0;
        chrome.Changed += (_, _) => changes++;
        chrome.PointerMoved(PointerZone.Video);
        Wait(chrome, 6);
        Assert.Equal(1, changes);
        Assert.False(chrome.BarsVisible);
        Assert.False(chrome.CursorVisible);

        // Nothing changed, nothing is reported.
        chrome.Tick();
        Assert.Equal(1, changes);

        chrome.SetBarsHiding(false);
        chrome.SetCursorHiding(false);
        Assert.Equal(3, changes);
        Assert.True(chrome.BarsVisible);
        Assert.True(chrome.CursorVisible);
    }

    /// <summary>
    /// A clock moved by the test.
    /// </summary>
    private sealed class TestTime : TimeProvider
    {
        /// <summary>
        /// The current timestamp in ticks.
        /// </summary>
        private long _ticks;

        /// <inheritdoc />
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        /// <inheritdoc />
        public override long GetTimestamp() => _ticks;

        /// <summary>
        /// Moves the time forward.
        /// </summary>
        /// <param name="value">The step.</param>
        public void Advance(TimeSpan value) => _ticks += value.Ticks;
    }
}
