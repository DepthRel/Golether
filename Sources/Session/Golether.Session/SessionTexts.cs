using System.Globalization;
using Golether.Core.Data.Enums;
using Golether.Core.Playback;
using Golether.Localization;

namespace Golether.Session;

/// <summary>
/// Formats event feed texts.
/// </summary>
public static class SessionTexts
{
    /// <summary>
    /// The prefix of the keys that word a playback change; the rest of the key is the <see cref="PlaybackCause"/>.
    /// </summary>
    private const string PlaybackPrefix = "Session.Playback.";

    /// <summary>
    /// The prefix of the keys that word a rejection; the rest of the key is the <see cref="RejectReason"/>.
    /// </summary>
    private const string RejectPrefix = "Session.Reject.";

    /// <summary>
    /// Formats a media position as <c>h:mm:ss</c>.
    /// </summary>
    /// <param name="position">The position.</param>
    /// <returns>The text.</returns>
    public static string FormatPosition(TimeSpan position)
        => ((int)position.TotalHours).ToString(CultureInfo.InvariantCulture) + position.ToString(@"\:mm\:ss", CultureInfo.InvariantCulture);

    /// <summary>
    /// Describes a playback change.
    /// </summary>
    /// <param name="state">The new state.</param>
    /// <param name="name">The name of the originator.</param>
    /// <returns>The text.</returns>
    public static string Describe(PlaybackState state, string name)
    {
        ArgumentNullException.ThrowIfNull(state);
        var key = Enum.IsDefined(state.Cause) ? PlaybackPrefix + state.Cause : PlaybackPrefix + nameof(PlaybackCause.Initial);
        return Texts.Format(key, name, FormatPosition(state.Position));
    }

    /// <summary>
    /// Words why the host did not admit a participant, in the language of this device.
    /// </summary>
    /// <param name="reason">The reason the host reported.</param>
    /// <returns>The text.</returns>
    public static string DescribeRejection(RejectReason reason)
        => Texts.Get(Enum.IsDefined(reason) ? RejectPrefix + reason : RejectPrefix + "Unknown");
}
