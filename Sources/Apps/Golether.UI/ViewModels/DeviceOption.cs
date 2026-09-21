using Golether.Localization;
using Golether.Media.Conference;

namespace Golether.UI.ViewModels;

/// <summary>
/// An entry of a device list.
/// </summary>
/// <param name="DeviceName">The name of the device; unused for the system default, which has no name of its own.</param>
/// <param name="Device">The device, or <see langword="null"/> for the system default.</param>
public sealed record DeviceOption(string DeviceName, CaptureDevice? Device)
{
    /// <summary>
    /// The entry of the system default device.
    /// </summary>
    public static readonly DeviceOption SystemDefault = new(string.Empty, null);

    /// <summary>
    /// Gets the display name; the entry of the system default device is worded in the language in use.
    /// </summary>
    public string Name => Device is null ? Texts.Get("Devices.SystemDefault") : DeviceName;

    /// <inheritdoc />
    public override string ToString() => Name;
}
