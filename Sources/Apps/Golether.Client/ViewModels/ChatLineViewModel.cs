using Golether.Localization;

namespace Golether.Client.ViewModels;

/// <summary>
/// A chat line.
/// </summary>
/// <param name="Id">The message identifier.</param>
/// <param name="Sender">The sender name.</param>
/// <param name="Text">The text.</param>
/// <param name="Time">The receive time.</param>
/// <param name="IsLocal">Whether this device sent it.</param>
/// <param name="IsSystem">Whether it is a note of this device.</param>
/// <param name="SenderColor">The colour of the sender, or <see langword="null"/> for a note.</param>
public sealed record ChatLineViewModel(string Id, string Sender, string Text, DateTimeOffset Time, bool IsLocal, bool IsSystem, string? SenderColor = null)
{
    /// <summary>
    /// Gets the time text.
    /// </summary>
    public string TimeText => Time.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Gets the name shown above the text.
    /// </summary>
    public string SenderText => IsSystem ? string.Empty : IsLocal ? Texts.Get("Chat.You") : Sender;

    /// <summary>
    /// Gets a value indicating whether another participant wrote the line.
    /// </summary>
    public bool IsFromOther => !IsSystem && !IsLocal;

    /// <summary>
    /// Gets a value indicating whether this device wrote the line.
    /// </summary>
    public bool IsOwn => !IsSystem && IsLocal;
}
