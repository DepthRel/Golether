using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Golether.Localization;
using Golether.UI.Services;

namespace Golether.UI.ViewModels.Dialogs;

/// <summary>
/// The avatar chooser: pick an image, move and zoom it inside the round frame; the view then renders the visible part
/// as a round avatar shown to others while the camera is off. The original image and its framing are kept, so opening
/// the chooser again starts from the original rather than from the cropped circle.
/// </summary>
public sealed partial class AvatarDialogViewModel : ObservableObject
{
    /// <summary>
    /// The side of the rendered avatar and of the crop frame, in pixels.
    /// </summary>
    public const int CropSize = 256;

    /// <summary>
    /// The smallest zoom: the image just fills the frame.
    /// </summary>
    public const double MinZoom = 1;

    /// <summary>
    /// The largest zoom.
    /// </summary>
    public const double MaxZoom = 4;

    /// <summary>
    /// Picks image files and shows errors.
    /// </summary>
    private readonly IDialogService _dialogs;

    /// <summary>
    /// Initializes a new instance of the <see cref="AvatarDialogViewModel"/> class.
    /// </summary>
    /// <param name="dialogs">Picks image files and shows errors.</param>
    /// <param name="current">The original image and framing to start from, or <see langword="null"/>.</param>
    public AvatarDialogViewModel(IDialogService dialogs, StoredAvatar? current = null)
    {
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        if (current is { Original.Length: > 0 } && TryDecode(current.Original) is { } bitmap)
        {
            OriginalBytes = current.Original;
            Source = bitmap;
            Zoom = Math.Clamp(current.Framing.Zoom, MinZoom, MaxZoom);
            OffsetX = current.Framing.OffsetX;
            OffsetY = current.Framing.OffsetY;
        }
    }

    /// <summary>
    /// Gets the original image bytes exactly as they were picked, or <see langword="null"/> before one is chosen.
    /// </summary>
    public byte[]? OriginalBytes { get; private set; }

    /// <summary>
    /// Gets or sets the chosen image, or <see langword="null"/> before one is picked.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm), nameof(HasImage))]
    public partial Bitmap? Source { get; set; }

    /// <summary>
    /// Gets or sets the zoom applied to the image inside the frame.
    /// </summary>
    [ObservableProperty]
    public partial double Zoom { get; set; } = MinZoom;

    /// <summary>
    /// Gets or sets the horizontal shift of the image inside the frame, in pixels.
    /// </summary>
    [ObservableProperty]
    public partial double OffsetX { get; set; }

    /// <summary>
    /// Gets or sets the vertical shift of the image inside the frame, in pixels.
    /// </summary>
    [ObservableProperty]
    public partial double OffsetY { get; set; }

    /// <summary>
    /// Gets a value indicating whether an image was chosen.
    /// </summary>
    public bool HasImage => Source is not null;

    /// <summary>
    /// Gets a value indicating whether the avatar can be saved.
    /// </summary>
    public bool CanConfirm => Source is not null && OriginalBytes is not null;

    /// <summary>
    /// Gets the title of the dialog.
    /// </summary>
    public string Title => Texts.Get("Avatar.Title");

    /// <summary>
    /// Gets the explanation shown above the frame.
    /// </summary>
    public string Hint => Texts.Get("Avatar.Hint");

    /// <summary>
    /// Opens an image and resets the framing.
    /// </summary>
    /// <returns>A task that completes when the image is chosen or the picker is closed.</returns>
    [RelayCommand]
    private async Task PickImageAsync()
    {
        var path = await _dialogs.PickImageFileAsync();
        if (path is null)
        {
            return;
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            var bitmap = TryDecode(bytes) ?? throw new NotSupportedException(Texts.Get("Avatar.BadImage.Title"));
            OriginalBytes = bytes;
            Source = bitmap;
            Zoom = MinZoom;
            OffsetX = 0;
            OffsetY = 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            await _dialogs.ShowErrorAsync(Texts.Get("Avatar.BadImage.Title"), ex.Message);
        }
    }

    /// <summary>
    /// Decodes image bytes, ignoring anything that is not a readable image.
    /// </summary>
    /// <param name="bytes">The image bytes.</param>
    /// <returns>The bitmap, or <see langword="null"/>.</returns>
    private static Bitmap? TryDecode(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            return new Bitmap(stream);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
