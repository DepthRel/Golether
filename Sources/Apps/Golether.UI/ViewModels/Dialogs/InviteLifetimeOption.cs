namespace Golether.UI.ViewModels.Dialogs;

/// <summary>
/// A lifetime option of an invitation.
/// </summary>
/// <param name="Title">The caption.</param>
/// <param name="Lifetime">The lifetime.</param>
public sealed record InviteLifetimeOption(string Title, TimeSpan Lifetime)
{
    /// <summary>
    /// Returns the caption.
    /// </summary>
    /// <returns>The caption.</returns>
    public override string ToString() => Title;
}
