using System.Collections.ObjectModel;

namespace Golether.Client.ViewModels;

/// <summary>
/// The tiles that show the cameras of the participants.
/// </summary>
public interface ICameraTiles
{
    /// <summary>
    /// Gets the tiles.
    /// </summary>
    ObservableCollection<ParticipantItemViewModel> Participants { get; }

    /// <summary>
    /// Gets a value indicating whether the camera of this device is off.
    /// </summary>
    bool CameraOff { get; }
}
