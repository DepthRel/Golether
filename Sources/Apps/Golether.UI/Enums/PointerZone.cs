using Golether.UI.Controls;

namespace Golether.UI.Enums;

/// <summary>
/// The part of the full-screen window the pointer is over, as <see cref="ChromeAutoHide"/> sees it.
/// </summary>
public enum PointerZone
{
    /// <summary>
    /// Anywhere that neither hides the cursor nor keeps the bars: the participants, the chat, outside the window.
    /// </summary>
    Elsewhere,

    /// <summary>
    /// The video picture: the cursor disappears here when it rests.
    /// </summary>
    Video,

    /// <summary>
    /// The top bar, the player bar, or the edge of the screen that brings them back.
    /// </summary>
    Bars,
}
