using Golether.UI.Enums;

namespace Golether.UI.Controls;

/// <summary>
/// Decides when the bars over the video and the cursor are shown.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>The two are switched on separately: the cursor hides during any session, the bars only in full screen.</item>
/// <item>While something holds them (an open menu, a drag on the timeline, the pen), everything is shown.</item>
/// <item>The bars are shown while the pointer is over them or at the edge that brings them back, and for
/// <see cref="Delay"/> after it left.</item>
/// <item>The cursor disappears after it rested over the video for <see cref="Delay"/>; anywhere else it stays.</item>
/// </list>
/// The class knows nothing about windows: the view reports where the pointer is and calls <see cref="Tick"/>.
/// </remarks>
public sealed class ChromeAutoHide
{
    /// <summary>
    /// The clock.
    /// </summary>
    private readonly TimeProvider _time;

    /// <summary>
    /// Whether the bars leave when the pointer is away from them.
    /// </summary>
    private bool _barsHiding;

    /// <summary>
    /// Whether the cursor leaves when it rests over the video.
    /// </summary>
    private bool _cursorHiding;

    /// <summary>
    /// Whether something keeps the bars and the cursor shown.
    /// </summary>
    private bool _held;

    /// <summary>
    /// Where the pointer was reported last.
    /// </summary>
    private PointerZone _zone;

    /// <summary>
    /// When the pointer was last over the bars, or the moment the countdown started anew.
    /// </summary>
    private long _barsSeen;

    /// <summary>
    /// When the pointer moved last.
    /// </summary>
    private long _moved;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChromeAutoHide"/> class.
    /// </summary>
    /// <param name="time">The clock.</param>
    /// <param name="delay">How long the bars and the cursor stay after the pointer left or stopped.</param>
    public ChromeAutoHide(TimeProvider time, TimeSpan delay)
    {
        _time = time ?? throw new ArgumentNullException(nameof(time));
        ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero);
        Delay = delay;
    }

    /// <summary>
    /// Raised when <see cref="BarsVisible"/> or <see cref="CursorVisible"/> changed.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Gets how long the bars and the cursor stay after the pointer left or stopped.
    /// </summary>
    public TimeSpan Delay { get; }

    /// <summary>
    /// Gets a value indicating whether anything may hide, so the movements of the pointer matter.
    /// </summary>
    public bool IsActive => _barsHiding || _cursorHiding;

    /// <summary>
    /// Gets a value indicating whether the top bar and the player bar are shown.
    /// </summary>
    public bool BarsVisible { get; private set; } = true;

    /// <summary>
    /// Gets a value indicating whether the cursor is shown.
    /// </summary>
    public bool CursorVisible { get; private set; } = true;

    /// <summary>
    /// Switches the hiding of the bars on or off. Switching it on starts the countdown, so the bars leave by
    /// themselves.
    /// </summary>
    /// <param name="hiding">Whether the bars lie over the video and leave when the pointer is away.</param>
    public void SetBarsHiding(bool hiding)
    {
        if (_barsHiding == hiding)
        {
            return;
        }

        _barsHiding = hiding;
        _barsSeen = _time.GetTimestamp();
        Update();
    }

    /// <summary>
    /// Switches the hiding of the cursor on or off.
    /// </summary>
    /// <param name="hiding">Whether the cursor leaves when it rests over the video.</param>
    public void SetCursorHiding(bool hiding)
    {
        if (_cursorHiding == hiding)
        {
            return;
        }

        _cursorHiding = hiding;
        _zone = PointerZone.Elsewhere;
        _moved = _time.GetTimestamp();
        Update();
    }

    /// <summary>
    /// Keeps everything shown while a menu is open, the timeline is dragged or the pen draws. Letting go starts the
    /// countdown anew.
    /// </summary>
    /// <param name="held">Whether something needs the bars and the cursor.</param>
    public void SetHeld(bool held)
    {
        if (_held == held)
        {
            return;
        }

        _held = held;
        Restart();
        Update();
    }

    /// <summary>
    /// Takes a movement of the pointer.
    /// </summary>
    /// <param name="zone">Where the pointer is now.</param>
    public void PointerMoved(PointerZone zone)
    {
        var now = _time.GetTimestamp();
        _moved = now;

        // Leaving the bars counts from the moment of leaving, not from the moment the pointer came to them.
        if (zone == PointerZone.Bars || _zone == PointerZone.Bars)
        {
            _barsSeen = now;
        }

        _zone = zone;
        Update();
    }

    /// <summary>
    /// Lets the time pass: hides what has been left alone for long enough.
    /// </summary>
    public void Tick() => Update();

    /// <summary>
    /// Starts both countdowns from now.
    /// </summary>
    private void Restart()
    {
        _barsSeen = _moved = _time.GetTimestamp();
    }

    /// <summary>
    /// Works out what is shown and reports a change.
    /// </summary>
    private void Update()
    {
        // Hidden bars come back only from their zone; a movement elsewhere does not bring them.
        var bars = !_barsHiding || _held || _zone == PointerZone.Bars ||
            (BarsVisible && _time.GetElapsedTime(_barsSeen) < Delay);
        var cursor = !_cursorHiding || _held || _zone != PointerZone.Video || _time.GetElapsedTime(_moved) < Delay;

        if (bars == BarsVisible && cursor == CursorVisible)
        {
            return;
        }

        BarsVisible = bars;
        CursorVisible = cursor;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
