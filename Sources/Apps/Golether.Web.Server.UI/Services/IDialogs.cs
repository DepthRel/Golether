namespace Golether.Web.Server.UI.Services;

/// <summary>
/// The questions the monitor asks the user.
/// </summary>
public interface IDialogs
{
    /// <summary>
    /// Asks where to save a file.
    /// </summary>
    /// <param name="suggestedName">The name offered.</param>
    /// <returns>The chosen file, or <see langword="null"/> when the user cancelled.</returns>
    Task<Stream?> SaveFileAsync(string suggestedName);

    /// <summary>
    /// Asks the user to confirm an action that is hard to undo.
    /// </summary>
    /// <param name="title">The question.</param>
    /// <param name="message">What the action does.</param>
    /// <param name="acceptText">The caption of the button that goes ahead.</param>
    /// <returns><see langword="true"/> when the user went ahead.</returns>
    Task<bool> ConfirmAsync(string title, string message, string acceptText);
}
