using Golether.Core.Data.Enums;

namespace Golether.Media.Conference;

/// <summary>
/// A capture device.
/// </summary>
/// <param name="Id">The backend identifier.</param>
/// <param name="Name">The name shown to the user.</param>
/// <param name="Kind">The device kind.</param>
public sealed record CaptureDevice(string Id, string Name, CaptureDeviceKind Kind);
