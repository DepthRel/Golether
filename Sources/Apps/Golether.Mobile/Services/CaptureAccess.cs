namespace Golether.Mobile.Services;

/// <summary>
/// What the system lets the application capture.
/// </summary>
/// <param name="Camera">Whether the camera may be used.</param>
/// <param name="Microphone">Whether the microphone may be used.</param>
public readonly record struct CaptureAccess(bool Camera, bool Microphone);
