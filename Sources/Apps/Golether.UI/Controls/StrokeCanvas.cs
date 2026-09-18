using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Golether.UI.ViewModels;

namespace Golether.UI.Controls;

/// <summary>
/// Draws the strokes of the pen over the video and collects the stroke of this device. It sits in the transparent
/// window above the video picture, because nothing drawn by Avalonia can be shown over mpv's own window.
/// </summary>
public sealed class StrokeCanvas : Control
{
    /// <summary>
    /// The width of a stroke.
    /// </summary>
    private const double Thickness = 4;

    /// <summary>
    /// Defines the <see cref="Source"/> property.
    /// </summary>
    public static readonly StyledProperty<DrawingViewModel?> SourceProperty =
        AvaloniaProperty.Register<StrokeCanvas, DrawingViewModel?>(nameof(Source));

    /// <summary>
    /// Defines the <see cref="Aspect"/> property.
    /// </summary>
    public static readonly StyledProperty<double> AspectProperty =
        AvaloniaProperty.Register<StrokeCanvas, double>(nameof(Aspect));

    /// <summary>
    /// The model the control is listening to.
    /// </summary>
    private DrawingViewModel? _attached;

    /// <summary>
    /// Whether an animation frame is already requested.
    /// </summary>
    private bool _frameRequested;

    /// <summary>
    /// Gets or sets the strokes to draw.
    /// </summary>
    public DrawingViewModel? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>
    /// Gets or sets the width-to-height ratio of the picture, or <c>0</c> to use the whole control.
    /// </summary>
    public double Aspect
    {
        get => GetValue(AspectProperty);
        set => SetValue(AspectProperty, value);
    }

    /// <summary>
    /// Gets the part of the control the picture takes, keeping its ratio.
    /// </summary>
    /// <param name="size">The size of the control.</param>
    /// <param name="aspect">The ratio of the picture, or <c>0</c>.</param>
    /// <returns>The picture rectangle.</returns>
    public static Rect PictureRect(Size size, double aspect)
    {
        if (aspect <= 0 || size.Width <= 0 || size.Height <= 0)
        {
            return new Rect(size);
        }

        var own = size.Width / size.Height;
        if (Math.Abs(own - aspect) < 0.0001)
        {
            return new Rect(size);
        }

        if (own > aspect)
        {
            var width = size.Height * aspect;
            return new Rect((size.Width - width) / 2, 0, width, size.Height);
        }

        var height = size.Width / aspect;
        return new Rect(0, (size.Height - height) / 2, size.Width, height);
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        // While the pen is out the whole area must answer hit tests, otherwise it would only draw on top of an
        // existing stroke. While it is put away the canvas must not answer them at all, or it would swallow the
        // clicks meant for the video.
        if (Source is { IsPenActive: true })
        {
            context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        }

        if (Source is not { } source || source.Strokes.Count == 0)
        {
            return;
        }

        var picture = PictureRect(Bounds.Size, Aspect);
        foreach (var stroke in source.Strokes)
        {
            Draw(context, stroke, picture);
        }

        // The fading has to run at the frame rate of the screen: the once-a-second refresh of the session would
        // make the line step down instead of melting away.
        RequestFrame();
    }

    /// <summary>
    /// Fades the strokes a little and repaints, as long as there is something on the picture.
    /// </summary>
    private void OnFrame()
    {
        _frameRequested = false;
        if (Source is not { } source || source.Strokes.Count == 0)
        {
            return;
        }

        source.Tick();
        InvalidateVisual();
    }

    /// <summary>
    /// Asks for the next animation frame while strokes are shown.
    /// </summary>
    private void RequestFrame()
    {
        if (_frameRequested || Source is not { Strokes.Count: > 0 } || TopLevel.GetTopLevel(this) is not { } top)
        {
            return;
        }

        _frameRequested = true;
        top.RequestAnimationFrame(_ => OnFrame());
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Source is not { IsPenActive: true } source || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var point = ToShare(e.GetPosition(this));
        source.BeginStroke(point.X, point.Y);
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (Source is not { IsPenActive: true } source)
        {
            return;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var point = ToShare(e.GetPosition(this));
        source.ExtendStroke(point.X, point.Y);
        e.Handled = true;
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (Source is not { } source)
        {
            return;
        }

        source.EndStroke();
        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        Source?.EndStroke();
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty)
        {
            Attach(change.GetNewValue<DrawingViewModel?>());
        }
        else if (change.Property == AspectProperty)
        {
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Listens to a model and stops listening to the previous one.
    /// </summary>
    /// <param name="source">The new model, or <see langword="null"/>.</param>
    private void Attach(DrawingViewModel? source)
    {
        if (_attached is { } previous)
        {
            previous.StrokesChanged -= OnStrokesChanged;
            previous.PropertyChanged -= OnSourcePropertyChanged;
        }

        _attached = source;
        if (source is not null)
        {
            source.StrokesChanged += OnStrokesChanged;
            source.PropertyChanged += OnSourcePropertyChanged;
        }

        UpdateCursor();
        InvalidateVisual();
    }

    /// <summary>
    /// Follows the pen being picked up and put down.
    /// </summary>
    /// <param name="sender">The model.</param>
    /// <param name="e">The property.</param>
    private void OnSourcePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DrawingViewModel.IsPenActive) or null)
        {
            UpdateCursor();
        }
    }

    /// <summary>
    /// Shows the pen instead of the usual cursor while the pen is picked up, and lets everything below through
    /// while it is put away.
    /// </summary>
    private void UpdateCursor()
    {
        var active = Source is { IsPenActive: true };
        Cursor = active ? PenCursor.Get() : Cursor.Default;
        IsHitTestVisible = active;
        InvalidateVisual();
    }

    /// <summary>
    /// Repaints the strokes.
    /// </summary>
    /// <param name="sender">The model.</param>
    /// <param name="e">Empty.</param>
    private void OnStrokesChanged(object? sender, EventArgs e)
    {
        InvalidateVisual();
        RequestFrame();
    }

    /// <summary>
    /// Turns a position in the control into a share of the picture.
    /// </summary>
    /// <param name="position">The position.</param>
    /// <returns>The shares, 0–1.</returns>
    private Point ToShare(Point position)
    {
        var picture = PictureRect(Bounds.Size, Aspect);
        if (picture.Width <= 0 || picture.Height <= 0)
        {
            return new Point(0, 0);
        }

        return new Point(
            Math.Clamp((position.X - picture.X) / picture.Width, 0, 1),
            Math.Clamp((position.Y - picture.Y) / picture.Height, 0, 1));
    }

    /// <summary>
    /// Draws one stroke.
    /// </summary>
    /// <param name="context">The drawing context.</param>
    /// <param name="stroke">The stroke.</param>
    /// <param name="picture">The picture rectangle.</param>
    private static void Draw(DrawingContext context, StrokeViewModel stroke, Rect picture)
    {
        var points = stroke.Points;
        if (points.Count == 0 || stroke.Opacity <= 0)
        {
            return;
        }

        var color = Color.TryParse(stroke.Color, out var parsed) ? parsed : Colors.White;
        var opacity = Math.Clamp(stroke.Opacity, 0, 1);

        // A dark halo under the stroke keeps it readable over a bright picture.
        var halo = new Pen(new SolidColorBrush(Colors.Black, opacity * 0.35), Thickness + 3)
        {
            LineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        var pen = new Pen(new SolidColorBrush(color, opacity), Thickness)
        {
            LineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };

        if (points.Count == 1)
        {
            var only = Map(points[0], picture);
            context.DrawEllipse(new SolidColorBrush(color, opacity), null, only, Thickness / 2, Thickness / 2);
            return;
        }

        var geometry = new StreamGeometry();
        using (var sink = geometry.Open())
        {
            sink.BeginFigure(Map(points[0], picture), false);
            for (var i = 1; i < points.Count; i++)
            {
                sink.LineTo(Map(points[i], picture));
            }

            sink.EndFigure(false);
        }

        context.DrawGeometry(null, halo, geometry);
        context.DrawGeometry(null, pen, geometry);
    }

    /// <summary>
    /// Turns a share of the picture into a position in the control.
    /// </summary>
    /// <param name="point">The point.</param>
    /// <param name="picture">The picture rectangle.</param>
    /// <returns>The position.</returns>
    private static Point Map(Sync.Protocol.StrokePoint point, Rect picture)
        => new(picture.X + (point.X * picture.Width), picture.Y + (point.Y * picture.Height));
}
