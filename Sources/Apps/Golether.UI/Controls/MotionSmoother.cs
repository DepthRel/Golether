namespace Golether.UI.Controls;

/// <summary>
/// Turns position updates that arrive a few times per second into smooth motion.
/// </summary>
/// <remarks>
/// Between updates the value keeps moving at the given speed (playback goes on). A new update that differs from the
/// shown value is reached with an ease-out curve instead of a jump, so seeks glide and small corrections are invisible.
/// </remarks>
public sealed class MotionSmoother
{
    /// <summary>
    /// How long a correction takes, in seconds.
    /// </summary>
    private readonly double _catchUp;

    /// <summary>
    /// Whether a target was set.
    /// </summary>
    private bool _initialized;

    /// <summary>
    /// The last target.
    /// </summary>
    private double _anchorValue;

    /// <summary>
    /// The time of the last target, in seconds.
    /// </summary>
    private double _anchorTime;

    /// <summary>
    /// The speed of the target, in units per second.
    /// </summary>
    private double _rate;

    /// <summary>
    /// The value shown when the current correction started.
    /// </summary>
    private double _fromValue;

    /// <summary>
    /// The start of the current correction, in seconds.
    /// </summary>
    private double _correctionStart = double.NegativeInfinity;

    /// <summary>
    /// Initializes a new instance of the <see cref="MotionSmoother"/> class.
    /// </summary>
    /// <param name="catchUpSeconds">How long a correction takes (0.3 s by default).</param>
    public MotionSmoother(double catchUpSeconds = 0.3)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(catchUpSeconds);
        _catchUp = catchUpSeconds;
    }

    /// <summary>
    /// Gets or sets the smallest value shown.
    /// </summary>
    public double Minimum { get; set; } = double.NegativeInfinity;

    /// <summary>
    /// Gets or sets the largest value shown.
    /// </summary>
    public double Maximum { get; set; } = double.PositiveInfinity;

    /// <summary>
    /// Gets a value indicating whether a target was set.
    /// </summary>
    public bool HasValue => _initialized;

    /// <summary>
    /// Sets a new target.
    /// </summary>
    /// <param name="value">The value reported now.</param>
    /// <param name="ratePerSecond">How fast the value moves until the next report (0 when it stands still).</param>
    /// <param name="now">The current time in seconds.</param>
    /// <param name="jump">Whether to show the value at once (no correction).</param>
    public void SetTarget(double value, double ratePerSecond, double now, bool jump = false)
    {
        var shown = _initialized ? ValueAt(now) : value;
        _anchorValue = value;
        _anchorTime = now;
        _rate = ratePerSecond;
        if (!_initialized || jump || Math.Abs(shown - value) < 1e-9)
        {
            _initialized = true;
            _correctionStart = double.NegativeInfinity;
            return;
        }

        _fromValue = shown;
        _correctionStart = now;
    }

    /// <summary>
    /// Returns the value to show.
    /// </summary>
    /// <param name="now">The current time in seconds.</param>
    /// <returns>The value.</returns>
    public double ValueAt(double now)
    {
        var target = _anchorValue + (_rate * Math.Max(0, now - _anchorTime));
        var progress = (now - _correctionStart) / _catchUp;
        var value = progress >= 1 || progress < 0
            ? target
            : _fromValue + ((target - _fromValue) * (1 - Math.Pow(1 - progress, 3)));
        return Math.Clamp(value, Minimum, Math.Max(Minimum, Maximum));
    }

    /// <summary>
    /// Checks whether the value still changes, so frames are needed.
    /// </summary>
    /// <param name="now">The current time in seconds.</param>
    /// <returns><see langword="true"/> while moving or correcting.</returns>
    public bool IsMoving(double now) => _initialized && (_rate != 0 || now - _correctionStart < _catchUp);
}
