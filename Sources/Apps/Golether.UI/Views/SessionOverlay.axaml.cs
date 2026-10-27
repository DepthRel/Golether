using Avalonia;
using Avalonia.Controls;

namespace Golether.UI.Views;

/// <summary>
/// The reactions and the strokes of the pen over what the screen shows: the film or a picture of a shown folder.
/// </summary>
public sealed partial class SessionOverlay : UserControl
{
    /// <summary>
    /// Defines the <see cref="Zoom"/> property.
    /// </summary>
    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<SessionOverlay, double>(nameof(Zoom), 1);

    /// <summary>
    /// Defines the <see cref="Center"/> property.
    /// </summary>
    public static readonly StyledProperty<Point> CenterProperty =
        AvaloniaProperty.Register<SessionOverlay, Point>(nameof(Center), new Point(0.5, 0.5));

    /// <summary>
    /// Initializes a new instance of the <see cref="SessionOverlay"/> class.
    /// </summary>
    public SessionOverlay()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Gets or sets how many times this person enlarged the picture under the layer; 1 over the film.
    /// </summary>
    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    /// <summary>
    /// Gets or sets the point of the enlarged picture in the middle, as shares of its width and height.
    /// </summary>
    public Point Center
    {
        get => GetValue(CenterProperty);
        set => SetValue(CenterProperty, value);
    }
}
