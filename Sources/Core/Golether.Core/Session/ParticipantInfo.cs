using Golether.Core.Identity;
using Golether.Localization;

namespace Golether.Core.Session;

/// <summary>
/// A participant of a session.
/// </summary>
/// <param name="PeerId">The device identifier.</param>
/// <param name="DisplayName">The name chosen by the participant.</param>
/// <param name="IsHost">Whether the participant hosts the session and shares the media.</param>
public sealed record ParticipantInfo(PeerId PeerId, string DisplayName, bool IsHost)
{
    /// <summary>
    /// The maximum length of a display name.
    /// </summary>
    public const int MAX_DISPLAY_NAME_LENGTH = 64;

    /// <summary>
    /// Normalizes a display name received from the user or the network.
    /// </summary>
    /// <param name="name">The raw name.</param>
    /// <returns>The trimmed name without control characters, at most <see cref="MAX_DISPLAY_NAME_LENGTH"/> characters;
    /// the localized default name when nothing is left.</returns>
    public static string NormalizeDisplayName(string? name)
    {
        var cleaned = new string((name ?? string.Empty).Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (cleaned.Length > MAX_DISPLAY_NAME_LENGTH)
        {
            cleaned = cleaned[..MAX_DISPLAY_NAME_LENGTH].TrimEnd();
        }

        return cleaned.Length == 0 ? Texts.Get("Session.DefaultParticipantName") : cleaned;
    }
}
