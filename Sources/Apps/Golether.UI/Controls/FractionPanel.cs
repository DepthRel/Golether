using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.VisualTree;

namespace Golether.UI.Controls;

/// <summary>
/// Places children horizontally at a fraction of the width (participant markers on the timeline) and moves them
/// smoothly: between updates the markers keep pace with playback, and jumps glide.
/// </summary>
public sealed class FractionPanel : Panel
{
    /// <summary>
    /// The position of a child, 0–1, or <see langword="null"/> to hide it.
    /// </summary>
    public static readonly AttachedProperty<double?> FractionProperty =
        AvaloniaProperty.RegisterAttached<FractionPanel, Control, double?>("Fraction");

    /// <summary>
    /// How fast markers move between updates, in fractions per second (0 while paused).
    /// </summary>
    public static readonly StyledProperty<double> AdvancePerSecondProperty =
        AvaloniaProperty.Register<FractionPanel, double>(nameof(AdvancePerSecond));

    /// <summary>
    /// The distance of the positions 0 and 1 from the edges (half the timeline thumb), so markers stand over it.
    /// </summary>
    public static readonly StyledProperty<double> InsetProperty =
        AvaloniaProperty.Register<FractionPanel, double>(nameof(Inset));

    /// <summary>
    /// The clock of the animation.
    /// </summary>
    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    /// <summary>
    /// The motion of each marker.
    /// </summary>
    private static readonly ConditionalWeakTable<Control, MotionSmoother> Motions = new();

    /// <summary>
    /// Whether an animation frame is requested.
    /// </summary>
    private bool _frameRequested;

    /// <summary>
    /// Initializes static members of the <see cref="FractionPanel"/> class.
    /// </summary>
    static FractionPanel()
    {
        FractionProperty.Changed.AddClassHandler<Control>(OnFractionChanged);
        AffectsArrange<FractionPanel>(InsetProperty);
        AdvancePerSecondProperty.Changed.AddClassHandler<FractionPanel>((panel, _) => panel.Retarget());
    }

    /// <summary>
    /// Gets or sets how fast markers move between updates, in fractions per second.
    /// </summary>
    public double AdvancePerSecond
    {
        get => GetValue(AdvancePerSecondProperty);
        set => SetValue(AdvancePerSecondProperty, value);
    }

    /// <summary>
    /// Gets or sets the distance of the positions 0 and 1 from the edges.
    /// </summary>
    public double Inset
    {
        get => GetValue(InsetProperty);
        set => SetValue(InsetProperty, value);
    }

    /// <summary>
    /// Gets the position of a child.
    /// </summary>
    /// <param name="control">The child.</param>
    /// <returns>The fraction.</returns>
    public static double? GetFraction(Control control) => control.GetValue(FractionProperty);

    /// <summary>
    /// Sets the position of a child.
    /// </summary>
    /// <param name="control">The child.</param>
    /// <param name="value">The fraction.</param>
    public static void SetFraction(Control control, double? value) => control.SetValue(FractionProperty, value);

    /// <summary>
    /// Returns the shown position of a child (for tests).
    /// </summary>
    /// <param name="child">The child.</param>
    /// <returns>The shown fraction, or <see langword="null"/>.</returns>
    public static double? GetShownFraction(Control child)
        => Motions.TryGetValue(Source(child), out var motion) && motion.HasValue ? motion.ValueAt(Clock.Elapsed.TotalSeconds) : null;

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var height = 0.0;
        foreach (var child in Children)
        {
            child.Measure(Size.Infinity);
            height = Math.Max(height, child.DesiredSize.Height);
        }

        return new Size(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, height);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var now = Clock.Elapsed.TotalSeconds;
        var moving = false;
        foreach (var child in Children)
        {
            var source = Source(child);
            if (GetFraction(source) is not { } fraction || double.IsNaN(fraction))
            {
                child.Arrange(new Rect(-10_000, 0, child.DesiredSize.Width, child.DesiredSize.Height));
                continue;
            }

            var motion = Motions.GetValue(source, _ => new MotionSmoother { Minimum = 0, Maximum = 1 });
            if (!motion.HasValue)
            {
                motion.SetTarget(fraction, AdvancePerSecond, now, jump: true);
            }

            moving |= motion.IsMoving(now);
            var usable = Math.Max(0, finalSize.Width - (2 * Inset));
            var x = Inset + (motion.ValueAt(now) * usable) - (child.DesiredSize.Width / 2);
            child.Arrange(new Rect(x, 0, child.DesiredSize.Width, child.DesiredSize.Height));
        }

        if (moving)
        {
            RequestFrame();
        }

        return finalSize;
    }

    /// <summary>
    /// Returns the control that carries the fraction (the item container or its content).
    /// </summary>
    /// <param name="child">The child.</param>
    /// <returns>The carrier.</returns>
    private static Control Source(Control child)
        => GetFraction(child) is null && child is ContentPresenter { Child: { } inner } ? inner : child;

    /// <summary>
    /// Starts the motion of a marker towards its new fraction.
    /// </summary>
    /// <param name="control">The marker.</param>
    /// <param name="e">The change.</param>
    private static void OnFractionChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        var panel = control.GetVisualAncestors().OfType<FractionPanel>().FirstOrDefault();
        if (e.NewValue is double fraction && Motions.TryGetValue(control, out var motion))
        {
            motion.SetTarget(fraction, panel?.AdvancePerSecond ?? 0, Clock.Elapsed.TotalSeconds);
        }

        panel?.RequestFrame();
    }

    /// <summary>
    /// Applies a new speed to every marker from its current position.
    /// </summary>
    private void Retarget()
    {
        var now = Clock.Elapsed.TotalSeconds;
        foreach (var child in Children)
        {
            if (Motions.TryGetValue(Source(child), out var motion) && motion.HasValue)
            {
                motion.SetTarget(motion.ValueAt(now), AdvancePerSecond, now, jump: true);
            }
        }

        RequestFrame();
    }

    /// <summary>
    /// Arranges again on the next frame.
    /// </summary>
    private void RequestFrame()
    {
        if (_frameRequested)
        {
            return;
        }

        if (TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }

        _frameRequested = true;
        top.RequestAnimationFrame(_ =>
        {
            _frameRequested = false;
            InvalidateArrange();
        });
    }
}
