using Golether.Core.Data.Enums;

namespace Golether.Media.Conference;

/// <summary>
/// A capture device.
/// </summary>
/// <param name="Id">The backend identifier.</param>
/// <param name="Name">The name shown to the user.</param>
/// <param name="Kind">The device kind.</param>
/// <param name="Rotation">How many degrees clockwise the picture must turn to stand upright: a phone holds its camera
/// sideways.</param>
public sealed record CaptureDevice(string Id, string Name, CaptureDeviceKind Kind, int Rotation = 0);
