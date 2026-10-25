using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Golether.UI.Controls;

/// <summary>
/// A short bright stripe that runs slowly around the border of the control under it, its tail fading into the border
/// itself. It lies over a button to draw the eye to it without covering anything: it takes no clicks and draws only
/// on the border line.
/// </summary>
public sealed class BorderChase : Control
{
    /// <summary>
    /// The length of the stripe as a share of the width of the control.
    /// </summary>
    public const double LENGTH_SHARE = 1.0 / 3;

    /// <summary>
    /// How many pieces the stripe is drawn of, each a little brighter than the one behind it.
    /// </summary>
    private const int SEGMENTS = 16;

    /// <summary>
    /// Whether the stripe runs; a stopped stripe is not drawn at all.
    /// </summary>
    public static readonly StyledProperty<bool> IsRunningProperty =
        AvaloniaProperty.Register<BorderChase, bool>(nameof(IsRunning));

    /// <summary>
    /// The colour of the head of the stripe.
    /// </summary>
    public static readonly StyledProperty<Color> ColorProperty =
        AvaloniaProperty.Register<BorderChase, Color>(nameof(Color), Colors.Orange);

    /// <summary>
    /// The corners of the border the stripe follows.
    /// </summary>
    public static readonly StyledProperty<CornerRadius> CornerRadiusProperty =
        AvaloniaProperty.Register<BorderChase, CornerRadius>(nameof(CornerRadius));

    /// <summary>
    /// The thickness of the stripe.
    /// </summary>
    public static readonly StyledProperty<double> ThicknessProperty =
        AvaloniaProperty.Register<BorderChase, double>(nameof(Thickness), 1.5);

    /// <summary>
    /// The pens of the pieces from the tail to the head, made again when the colour or the thickness changes.
    /// </summary>
    private Pen[]? _pens;

    /// <summary>
    /// Where the head of the stripe is: the share of the way around the border, from <c>0</c> to <c>1</c>.
    /// </summary>
    private double _phase;

    /// <summary>
    /// The time of the previous animation frame, or <see langword="null"/> before the first one.
    /// </summary>
    private TimeSpan? _lastFrame;

    /// <summary>
    /// Whether the next animation frame has been asked for.
    /// </summary>
    private bool _frameRequested;

    /// <summary>
    /// Initializes static members of the <see cref="BorderChase"/> class.
    /// </summary>
    static BorderChase()
    {
        AffectsRender<BorderChase>(CornerRadiusProperty);
        IsHitTestVisibleProperty.OverrideDefaultValue<BorderChase>(false);
        IsRunningProperty.Changed.AddClassHandler<BorderChase>((chase, _) => chase.Restart());
        ColorProperty.Changed.AddClassHandler<BorderChase>((chase, _) => chase.ResetPens());
        ThicknessProperty.Changed.AddClassHandler<BorderChase>((chase, _) => chase.ResetPens());
    }

    /// <summary>
    /// Gets or sets how long the stripe takes to run once around the border.
    /// </summary>
    public static TimeSpan LapDuration { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Gets or sets a value indicating whether the stripe runs.
    /// </summary>
    public bool IsRunning
    {
        get => GetValue(IsRunningProperty);
        set => SetValue(IsRunningProperty, value);
    }

    /// <summary>
    /// Gets or sets the colour of the head of the stripe.
    /// </summary>
    public Color Color
    {
        get => GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    /// <summary>
    /// Gets or sets the corners of the border the stripe follows.
    /// </summary>
    public CornerRadius CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    /// <summary>
    /// Gets or sets the thickness of the stripe.
    /// </summary>
    public double Thickness
    {
        get => GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    /// <summary>
    /// Moves the head of the stripe on by the time that passed.
    /// </summary>
    /// <param name="phase">Where the head was, as a share of the way around.</param>
    /// <param name="elapsed">The time that passed.</param>
    /// <param name="lap">How long one way around takes.</param>
    /// <returns>Where the head is now, from <c>0</c> to <c>1</c>.</returns>
    internal static double Advance(double phase, TimeSpan elapsed, TimeSpan lap)
    {
        if (lap <= TimeSpan.Zero || elapsed <= TimeSpan.Zero)
        {
            return phase;
        }

        var next = (phase + elapsed / lap) % 1;
        return next < 0 ? next + 1 : next;
    }

    /// <summary>
    /// Measures the way around a rectangle with rounded corners.
    /// </summary>
    /// <param name="rect">The rectangle.</param>
    /// <param name="radius">The radius of the corners, no more than half of the shorter side.</param>
    /// <returns>The length of the way.</returns>
    internal static double Perimeter(Rect rect, double radius)
        => 2 * (rect.Width - 2 * radius) + 2 * (rect.Height - 2 * radius) + 2 * Math.PI * radius;

    /// <summary>
    /// Finds the point at a distance along the border of a rectangle with rounded corners, clockwise from where the
    /// top-left corner ends.
    /// </summary>
    /// <param name="rect">The rectangle.</param>
    /// <param name="radius">The radius of the corners, no more than half of the shorter side.</param>
    /// <param name="distance">The distance; any value, it wraps around the border.</param>
    /// <returns>The point.</returns>
    internal static Point PointAt(Rect rect, double radius, double distance)
    {
        var across = rect.Width - 2 * radius;
        var down = rect.Height - 2 * radius;
        var arc = Math.PI * radius / 2;
        var perimeter = Perimeter(rect, radius);
        if (perimeter <= 0)
        {
            return rect.TopLeft;
        }

        var s = distance % perimeter;
        if (s < 0)
        {
            s += perimeter;
        }

        // The sides and the corners in turn: top, top-right, right, bottom-right, bottom, bottom-left, left, top-left.
        (double Length, Func<double, Point> Point)[] parts =
        [
            (across, d => new Point(rect.Left + radius + d, rect.Top)),
            (arc, d => Corner(rect.Right - radius, rect.Top + radius, -90, d)),
            (down, d => new Point(rect.Right, rect.Top + radius + d)),
            (arc, d => Corner(rect.Right - radius, rect.Bottom - radius, 0, d)),
            (across, d => new Point(rect.Right - radius - d, rect.Bottom)),
            (arc, d => Corner(rect.Left + radius, rect.Bottom - radius, 90, d)),
            (down, d => new Point(rect.Left, rect.Bottom - radius - d)),
            (arc, d => Corner(rect.Left + radius, rect.Top + radius, 180, d)),
        ];
        foreach (var (length, point) in parts)
        {
            if (s < length)
            {
                return point(s);
            }

            s -= length;
        }

        return new Point(rect.Left + radius, rect.Top);

        Point Corner(double centerX, double centerY, double startDegrees, double along)
        {
            var angle = (startDegrees + 90 * along / arc) * Math.PI / 180;
            return new Point(centerX + radius * Math.Cos(angle), centerY + radius * Math.Sin(angle));
        }
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (!IsRunning)
        {
            return;
        }

        // The stripe runs along the middle of a border line of its own thickness.
        var thickness = Math.Max(0.5, Thickness);
        var rect = new Rect(Bounds.Size).Deflate(thickness / 2);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        var radius = Math.Clamp(CornerRadius.TopLeft - thickness / 2, 0, Math.Min(rect.Width, rect.Height) / 2);
        var length = Bounds.Width * LENGTH_SHARE;
        var head = _phase * Perimeter(rect, radius);
        var pens = _pens ??= CreatePens(thickness);

        for (var i = 0; i < SEGMENTS; i++)
        {
            var from = head - length + length * i / SEGMENTS;
            var to = head - length + length * (i + 1) / SEGMENTS;
            context.DrawLine(pens[i], PointAt(rect, radius, from), PointAt(rect, radius, to));
        }
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Restart();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _lastFrame = null;
    }

    /// <summary>
    /// Starts the stripe from where it was, or clears it when it stopped.
    /// </summary>
    private void Restart()
    {
        _lastFrame = null;
        RequestFrame();
        InvalidateVisual();
    }

    /// <summary>
    /// Asks for the next animation frame while the stripe runs on screen.
    /// </summary>
    private void RequestFrame()
    {
        if (_frameRequested || !IsRunning || TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }

        _frameRequested = true;
        top.RequestAnimationFrame(OnFrame);
    }

    /// <summary>
    /// Moves the stripe on and asks for the next frame.
    /// </summary>
    /// <param name="time">The time of the frame.</param>
    private void OnFrame(TimeSpan time)
    {
        _frameRequested = false;
        if (!IsRunning || VisualRoot is null)
        {
            _lastFrame = null;
            InvalidateVisual();
            return;
        }

        if (_lastFrame is { } last)
        {
            _phase = Advance(_phase, time - last, LapDuration);
        }

        _lastFrame = time;
        InvalidateVisual();
        RequestFrame();
    }

    /// <summary>
    /// Forgets the pens, so the next frame makes them in the new colour or thickness.
    /// </summary>
    private void ResetPens()
    {
        _pens = null;
        InvalidateVisual();
    }

    /// <summary>
    /// Makes the pens of the pieces: the tail almost transparent, the head in the full colour.
    /// </summary>
    /// <param name="thickness">The thickness of the stripe.</param>
    /// <returns>The pens from the tail to the head.</returns>
    private Pen[] CreatePens(double thickness)
        => [.. Enumerable.Range(1, SEGMENTS).Select(i => new Pen(new ImmutableSolidColorBrush(Color, (double)i / SEGMENTS), thickness))];
}
