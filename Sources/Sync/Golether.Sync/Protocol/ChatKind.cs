namespace Golether.Sync.Protocol;

/// <summary>
/// The kind of a chat message.
/// </summary>
public enum ChatKind
{
    /// <summary>
    /// A text line.
    /// </summary>
    Text = 0,

    /// <summary>
    /// A reaction shown over the video for a moment.
    /// </summary>
    Reaction = 1,
}
