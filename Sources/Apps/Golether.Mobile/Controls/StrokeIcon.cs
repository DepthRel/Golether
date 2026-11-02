using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Golether.Mobile.Controls;

/// <summary>
/// An icon of lines on a 24 by 24 field, in the colour of the text around it, with the round ends of the logo.
/// </summary>
public sealed class StrokeIcon : Control
{
    /// <summary>
    /// The side of the field the icons are drawn on.
    /// </summary>
    private const double FIELD = 24;

    /// <summary>
    /// The lines of the icon.
    /// </summary>
    public static readonly StyledProperty<Geometry?> DataProperty = AvaloniaProperty.Register<StrokeIcon, Geometry?>(nameof(Data));

    /// <summary>
    /// The width of the lines on the field.
    /// </summary>
    public static readonly StyledProperty<double> ThicknessProperty = AvaloniaProperty.Register<StrokeIcon, double>(nameof(Thickness), 2);

    /// <summary>
    /// Whether the shape is filled as well.
    /// </summary>
    public static readonly StyledProperty<bool> IsFilledProperty = AvaloniaProperty.Register<StrokeIcon, bool>(nameof(IsFilled));

    /// <summary>
    /// The colour, inherited from the text around the icon.
    /// </summary>
    public static readonly StyledProperty<IBrush?> ForegroundProperty = TextElement.ForegroundProperty.AddOwner<StrokeIcon>();

    /// <summary>
    /// Initializes static members of the <see cref="StrokeIcon"/> class.
    /// </summary>
    static StrokeIcon() => AffectsRender<StrokeIcon>(DataProperty, ThicknessProperty, IsFilledProperty, ForegroundProperty);

    /// <summary>
    /// Gets or sets the lines of the icon.
    /// </summary>
    public Geometry? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    /// <summary>
    /// Gets or sets the width of the lines on the field.
    /// </summary>
    public double Thickness
    {
        get => GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the shape is filled as well.
    /// </summary>
    public bool IsFilled
    {
        get => GetValue(IsFilledProperty);
        set => SetValue(IsFilledProperty, value);
    }

    /// <summary>
    /// Gets or sets the colour.
    /// </summary>
    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (Data is not { } data || Foreground is not { } brush)
        {
            return;
        }

        var side = Math.Min(Bounds.Width, Bounds.Height);
        var scale = side / FIELD;
        var left = (Bounds.Width - side) / 2;
        var top = (Bounds.Height - side) / 2;
        var pen = new Pen(brush, Thickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(left, top)))
        {
            context.DrawGeometry(IsFilled ? brush : null, pen, data);
        }
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize) => new(FIELD, FIELD);
}
