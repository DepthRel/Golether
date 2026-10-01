namespace Golether.UI.Services;

/// <summary>
/// How the chosen image is framed inside the round avatar: the values fed to the crop view so re-opening it shows the
/// same position.
/// </summary>
/// <param name="Zoom">The zoom applied to the image.</param>
/// <param name="OffsetX">The horizontal shift, in pixels of the crop frame.</param>
/// <param name="OffsetY">The vertical shift, in pixels of the crop frame.</param>
public sealed record AvatarFraming(double Zoom, double OffsetX, double OffsetY);
