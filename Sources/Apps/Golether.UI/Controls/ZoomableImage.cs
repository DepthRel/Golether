using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Golether.UI.Controls;

/// <summary>
/// A picture of a shown folder that this person can enlarge and move: the wheel enlarges around the pointer, a drag
/// moves the enlarged picture. Only this screen changes; another picture starts fitted whole again.
/// </summary>
public sealed class ZoomableImage : Control
{
    /// <summary>
    /// The largest enlargement.
    /// </summary>
    public const double MAX_ZOOM = 8;

    /// <summary>
    /// The enlargement of one step of the wheel or of a key.
    /// </summary>
    public const double STEP = 1.25;

    /// <summary>
    /// Defines the <see cref="Source"/> property.
    /// </summary>
    public static readonly StyledProperty<IImage?> SourceProperty =
        AvaloniaProperty.Register<ZoomableImage, IImage?>(nameof(Source));

    /// <summary>
    /// Defines the <see cref="Zoom"/> property.
    /// </summary>
    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<ZoomableImage, double>(nameof(Zoom), 1);

    /// <summary>
    /// Defines the <see cref="Center"/> property.
    /// </summary>
    public static readonly StyledProperty<Point> CenterProperty =
        AvaloniaProperty.Register<ZoomableImage, Point>(nameof(Center), new Point(0.5, 0.5));

    /// <summary>
    /// Where the drag of the enlarged picture was last, or <see langword="null"/> while nothing is dragged.
    /// </summary>
    private Point? _dragFrom;

    /// <summary>
    /// Initializes static members of the <see cref="ZoomableImage"/> class.
    /// </summary>
    static ZoomableImage()
    {
        AffectsRender<ZoomableImage>(SourceProperty, ZoomProperty, CenterProperty);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ZoomableImage"/> class.
    /// </summary>
    public ZoomableImage()
    {
        ClipToBounds = true;
    }

    /// <summary>
    /// Gets or sets the picture.
    /// </summary>
    public IImage? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>
    /// Gets or sets how many times the fitted picture is enlarged, from 1 to <see cref="MAX_ZOOM"/>.
    /// </summary>
    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    /// <summary>
    /// Gets or sets the point of the picture in the middle of the control, as shares of its width and height.
    /// </summary>
    public Point Center
    {
        get => GetValue(CenterProperty);
        set => SetValue(CenterProperty, value);
    }

    /// <summary>
    /// Gets the width of the picture divided by its height, or <c>0</c> without a picture.
    /// </summary>
    private double Aspect => Source is { Size: { Width: > 0, Height: > 0 } size } ? size.Width / size.Height : 0;

    /// <summary>
    /// Enlarges or reduces the picture around the middle of the control.
    /// </summary>
    /// <param name="factor">The factor; above 1 enlarges.</param>
    public void ZoomBy(double factor) => ZoomAt(new Point(Bounds.Width / 2, Bounds.Height / 2), factor);

    /// <summary>
    /// Fits the picture whole again.
    /// </summary>
    public void Reset()
    {
        Zoom = 1;
        Center = new Point(0.5, 0.5);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        // The whole control answers the pointer, so the wheel and a drag work beside the picture as well.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        if (Source is { } source && Aspect > 0)
        {
            context.DrawImage(source, new Rect(source.Size), PictureFrame.Place(Bounds.Size, Aspect, Zoom, Center));
        }
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty)
        {
            _dragFrom = null;
            Reset();
        }
        else if (change.Property == ZoomProperty)
        {
            Cursor = Zoom > 1 ? new Cursor(StandardCursorType.SizeAll) : Cursor.Default;
        }
    }

    /// <inheritdoc />
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (Source is null || e.Delta.Y == 0)
        {
            return;
        }

        ZoomAt(e.GetPosition(this), Math.Pow(STEP, e.Delta.Y));
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Zoom > 1 && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.ClickCount == 1)
        {
            _dragFrom = e.GetPosition(this);
            e.Pointer.Capture(this);
        }
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragFrom is not { } from)
        {
            return;
        }

        var position = e.GetPosition(this);
        var picture = PictureFrame.Place(Bounds.Size, Aspect, Zoom, Center);
        if (picture.Width > 0 && picture.Height > 0)
        {
            SetView(Zoom, new Point(Center.X - ((position.X - from.X) / picture.Width), Center.Y - ((position.Y - from.Y) / picture.Height)));
        }

        _dragFrom = position;
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragFrom is not null)
        {
            _dragFrom = null;
            e.Pointer.Capture(null);
        }
    }

    /// <inheritdoc />
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragFrom = null;
    }

    /// <summary>
    /// Enlarges or reduces the picture so that the point of it under a position stays there.
    /// </summary>
    /// <param name="position">The position in the control.</param>
    /// <param name="factor">The factor; above 1 enlarges.</param>
    private void ZoomAt(Point position, double factor)
    {
        var picture = PictureFrame.Place(Bounds.Size, Aspect, Zoom, Center);
        var fitted = PictureFrame.Fit(Bounds.Size, Aspect);
        if (picture.Width <= 0 || picture.Height <= 0 || Aspect <= 0)
        {
            return;
        }

        var shareX = (position.X - picture.X) / picture.Width;
        var shareY = (position.Y - picture.Y) / picture.Height;
        var zoom = Math.Clamp(Zoom * factor, 1, MAX_ZOOM);
        var width = fitted.Width * zoom;
        var height = fitted.Height * zoom;
        var left = position.X - (shareX * width);
        var top = position.Y - (shareY * height);
        SetView(zoom, new Point(((Bounds.Width / 2) - left) / width, ((Bounds.Height / 2) - top) / height));
    }

    /// <summary>
    /// Takes an enlargement and a middle point, keeping the middle point the picture really has after it was kept
    /// from leaving an empty edge.
    /// </summary>
    /// <param name="zoom">The enlargement.</param>
    /// <param name="center">The wanted middle point.</param>
    private void SetView(double zoom, Point center)
    {
        var picture = PictureFrame.Place(Bounds.Size, Aspect, zoom, center);
        Zoom = zoom;
        Center = zoom <= 1 || picture.Width <= 0 || picture.Height <= 0
            ? new Point(0.5, 0.5)
            : new Point(((Bounds.Width / 2) - picture.X) / picture.Width, ((Bounds.Height / 2) - picture.Y) / picture.Height);
    }
}
