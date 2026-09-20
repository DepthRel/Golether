using Golether.Media.Conference;

namespace Golether.UI.ViewModels;

/// <summary>
/// Lists the capture devices and applies the chosen camera and microphone.
/// </summary>
public interface ICaptureDeviceSelector
{
    /// <summary>
    /// Gets a value indicating whether cameras and voices work.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Lists the cameras and microphones.
    /// </summary>
    /// <returns>The devices.</returns>
    IReadOnlyList<CaptureDevice> GetDevices();

    /// <summary>
    /// Chooses the capture devices.
    /// </summary>
    /// <param name="camera">The camera, or <see langword="null"/> for the system default.</param>
    /// <param name="microphone">The microphone, or <see langword="null"/> for the system default.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when capture uses the devices.</returns>
    Task SelectDevicesAsync(CaptureDevice? camera, CaptureDevice? microphone, CancellationToken cancellationToken);
}
