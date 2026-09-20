using Golether.Media.Conference;

namespace Golether.UI.ViewModels;

/// <summary>
/// An entry of a device list.
/// </summary>
/// <param name="Name">The display name.</param>
/// <param name="Device">The device, or <see langword="null"/> for the system default.</param>
public sealed record DeviceOption(string Name, CaptureDevice? Device)
{
    /// <summary>
    /// The entry of the system default device.
    /// </summary>
    public static readonly DeviceOption SystemDefault = new("Как в системе", null);

    /// <inheritdoc />
    public override string ToString() => Name;
}
