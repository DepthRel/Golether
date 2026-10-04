using Avalonia.Controls;

namespace Golether.UI.Views;

/// <summary>
/// A window with the camera of one participant, to look at it larger or on another screen. It only shows the picture
/// the participant tile already has: closing it changes nothing in the session.
/// </summary>
public sealed partial class ParticipantVideoWindow : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ParticipantVideoWindow"/> class.
    /// </summary>
    public ParticipantVideoWindow()
    {
        InitializeComponent();
    }
}
