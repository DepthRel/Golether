using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Golether.Client.Services;

namespace Golether.Mobile.ViewModels;

/// <summary>
/// The round frame of the picture of the user: the chosen image moves under a finger and grows under two inside the
/// circle, and the view renders the circle as the picture the others see while the camera is off.
/// </summary>
public sealed partial class AvatarEditorViewModel : ObservableObject
{
    /// <summary>
    /// The side of the frame, in the units of the screen.
    /// </summary>
    public const double CROP_SIZE = 256;

    /// <summary>
    /// The smallest enlargement: the image just covers the frame.
    /// </summary>
    public const double MIN_ZOOM = 1;

    /// <summary>
    /// The largest enlargement.
    /// </summary>
    public const double MAX_ZOOM = 4;

    /// <summary>
    /// The longest side the image is decoded at: the frame needs no more, and a photo of a phone would take a lot of
    /// memory whole.
    /// </summary>
    private const int MAX_DECODED_SIDE = 1024;

    /// <summary>
    /// Initializes a new instance of the <see cref="AvatarEditorViewModel"/> class.
    /// </summary>
    /// <param name="original">The image as it was chosen.</param>
    /// <param name="source">The image decoded upright.</param>
    private AvatarEditorViewModel(byte[] original, Bitmap source)
    {
        Original = original;
        Source = source;
    }

    /// <summary>
    /// Gets the image exactly as it was chosen, kept with the picture so it can be framed again.
    /// </summary>
    public byte[] Original { get; }

    /// <summary>
    /// Gets the image, upright as the camera held it.
    /// </summary>
    public Bitmap Source { get; }

    /// <summary>
    /// Gets or sets the enlargement of the image inside the frame.
    /// </summary>
    [ObservableProperty]
    public partial double Zoom { get; set; } = MIN_ZOOM;

    /// <summary>
    /// Gets or sets the horizontal shift of the image inside the frame.
    /// </summary>
    [ObservableProperty]
    public partial double OffsetX { get; set; }

    /// <summary>
    /// Gets or sets the vertical shift of the image inside the frame.
    /// </summary>
    [ObservableProperty]
    public partial double OffsetY { get; set; }

    /// <summary>
    /// Gets how the image is framed, kept with the picture.
    /// </summary>
    public AvatarFraming Framing => new(Zoom, OffsetX, OffsetY);

    /// <summary>
    /// Opens an image in the frame: a new one in the middle, or the kept one where the user left it.
    /// </summary>
    /// <param name="original">The bytes of the image.</param>
    /// <param name="decoder">Decodes pictures upright.</param>
    /// <param name="framing">How the image was framed before, or <see langword="null"/> for a new one.</param>
    /// <returns>The frame, or <see langword="null"/> when the bytes are no picture this device reads.</returns>
    public static AvatarEditorViewModel? TryOpen(byte[] original, IPictureDecoder decoder, AvatarFraming? framing = null)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(decoder);

        if (decoder.Decode(original, MAX_DECODED_SIDE) is not { } source)
        {
            return null;
        }

        var editor = new AvatarEditorViewModel(original, source);
        if (framing is not null)
        {
            editor.Zoom = Math.Clamp(framing.Zoom, MIN_ZOOM, MAX_ZOOM);
            editor.OffsetX = framing.OffsetX;
            editor.OffsetY = framing.OffsetY;
            editor.KeepCovered();
        }

        return editor;
    }

    /// <summary>
    /// Moves the image by the way a finger went.
    /// </summary>
    /// <param name="dx">The way along.</param>
    /// <param name="dy">The way down.</param>
    public void MoveBy(double dx, double dy)
    {
        OffsetX += dx;
        OffsetY += dy;
        KeepCovered();
    }

    /// <summary>
    /// Enlarges or shrinks the image by a step of two fingers.
    /// </summary>
    /// <param name="factor">The step, above one to enlarge.</param>
    public void ZoomBy(double factor)
    {
        if (factor > 0)
        {
            Zoom = Math.Clamp(Zoom * factor, MIN_ZOOM, MAX_ZOOM);
        }
    }

    /// <summary>
    /// Keeps the shift inside what the enlargement allows when the enlargement changes.
    /// </summary>
    /// <param name="value">The new enlargement.</param>
    partial void OnZoomChanged(double value) => KeepCovered();

    /// <summary>
    /// Keeps the frame covered: the image is a square of the frame enlarged around its middle, and its edge never comes
    /// inside the circle.
    /// </summary>
    private void KeepCovered()
    {
        var reach = ((CROP_SIZE * Zoom) - CROP_SIZE) / 2;

        OffsetX = Math.Clamp(OffsetX, -reach, reach);
        OffsetY = Math.Clamp(OffsetY, -reach, reach);
    }
}
