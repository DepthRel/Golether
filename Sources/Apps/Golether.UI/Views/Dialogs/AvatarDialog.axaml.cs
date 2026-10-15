using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Golether.UI.Services;
using Golether.UI.ViewModels.Dialogs;

namespace Golether.UI.Views.Dialogs;

/// <summary>
/// The avatar chooser window: drags and zooms the image inside the round frame and renders the visible circle to a
/// PNG when the user saves. Returns the round avatar together with the original and its framing, or
/// <see langword="null"/> when cancelled.
/// </summary>
public sealed partial class AvatarDialog : Window
{
    /// <summary>
    /// The pointer position where the current drag began, or <see langword="null"/> when not dragging.
    /// </summary>
    private Point? _dragStart;

    /// <summary>
    /// The image offset when the current drag began.
    /// </summary>
    private (double X, double Y) _offsetAtStart;

    /// <summary>
    /// Initializes a new instance of the <see cref="AvatarDialog"/> class.
    /// </summary>
    public AvatarDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// The view model, or <see langword="null"/> before it is set.
    /// </summary>
    private AvatarDialogViewModel? Model => DataContext as AvatarDialogViewModel;

    /// <summary>
    /// Begins a drag that pans the image.
    /// </summary>
    /// <param name="sender">The crop area.</param>
    /// <param name="e">The event data.</param>
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Model is not { HasImage: true } model)
        {
            return;
        }

        _dragStart = e.GetPosition(CropArea);
        _offsetAtStart = (model.OffsetX, model.OffsetY);
        e.Pointer.Capture(CropArea);
    }

    /// <summary>
    /// Pans the image while dragging.
    /// </summary>
    /// <param name="sender">The crop area.</param>
    /// <param name="e">The event data.</param>
    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragStart is not { } start || Model is not { } model)
        {
            return;
        }

        var current = e.GetPosition(CropArea);
        model.OffsetX = _offsetAtStart.X + (current.X - start.X);
        model.OffsetY = _offsetAtStart.Y + (current.Y - start.Y);
    }

    /// <summary>
    /// Ends the drag.
    /// </summary>
    /// <param name="sender">The crop area.</param>
    /// <param name="e">The event data.</param>
    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragStart = null;
        e.Pointer.Capture(null);
    }

    /// <summary>
    /// Zooms the image with the mouse wheel.
    /// </summary>
    /// <param name="sender">The crop area.</param>
    /// <param name="e">The event data.</param>
    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (Model is not { HasImage: true } model)
        {
            return;
        }

        model.Zoom = Math.Clamp(model.Zoom + (e.Delta.Y > 0 ? 0.1 : -0.1), AvatarDialogViewModel.MIN_ZOOM, AvatarDialogViewModel.MAX_ZOOM);
        e.Handled = true;
    }

    /// <summary>
    /// Renders the visible circle to a PNG and closes with its bytes.
    /// </summary>
    /// <param name="sender">The save button.</param>
    /// <param name="e">The event data.</param>
    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (Model is not { CanConfirm: true, OriginalBytes: { } original } model)
        {
            return;
        }

        var size = AvatarDialogViewModel.CROP_SIZE;
        using var target = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        target.Render(CropArea);
        using var stream = new MemoryStream();
        target.Save(stream, PngBitmapEncoderOptions.Default);
        Close(new AvatarSelection(stream.ToArray(), original, new AvatarFraming(model.Zoom, model.OffsetX, model.OffsetY)));
    }

    /// <summary>
    /// Closes without choosing an avatar.
    /// </summary>
    /// <param name="sender">The cancel button.</param>
    /// <param name="e">The event data.</param>
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
