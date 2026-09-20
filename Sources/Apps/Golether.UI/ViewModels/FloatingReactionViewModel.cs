namespace Golether.UI.ViewModels;

/// <summary>
/// A reaction floating over the video.
/// </summary>
/// <param name="Emoji">The reaction.</param>
/// <param name="Sender">The sender name.</param>
/// <param name="Lane">The horizontal lane, 0–3, so simultaneous reactions do not overlap.</param>
/// <param name="ShownAt">When it appeared.</param>
public sealed record FloatingReactionViewModel(string Emoji, string Sender, int Lane, DateTimeOffset ShownAt)
{
    /// <summary>
    /// Gets the horizontal offset in the overlay.
    /// </summary>
    public double Offset => Lane * 64;
}
