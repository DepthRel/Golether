using Golether.Core.Media;
using Golether.Core.Playback;
using Golether.Core.Session;
using Golether.Transports.Relay;

namespace Golether.Sync.Protocol;

/// <summary>
/// The participant has been admitted.
/// </summary>
/// <param name="SessionName">The session name.</param>
/// <param name="Participants">The current participants, host included.</param>
/// <param name="Playback">The authoritative playback state.</param>
/// <param name="Media">The shared media, or <see langword="null"/> when nothing is shared yet.</param>
/// <param name="MediaDataStreams">The number of parallel media streams the participant may open.</param>
public sealed record WelcomeMessage(
    string SessionName,
    IReadOnlyList<ParticipantInfo> Participants,
    PlaybackState Playback,
    MediaDescriptor? Media,
    int MediaDataStreams) : SessionMessage
{
    /// <summary>
    /// Gets the TURN relay of the host for cameras and voices, or <see langword="null"/>. Only admitted participants
    /// receive these credentials.
    /// </summary>
    public RelayCredentials? Relay { get; init; }

    /// <summary>
    /// Gets the ticket that lets this participant come back to the same session without a new invitation, or
    /// <see langword="null"/>. It is valid while the session runs and only for this device.
    /// </summary>
    public string? ReconnectTicket { get; init; }

    /// <summary>
    /// Returns the relay when its fields are sane.
    /// </summary>
    /// <returns>The relay or <see langword="null"/>.</returns>
    public RelayCredentials? GetValidRelay()
        => Relay is { Port: > 0 and <= 65535, Username.Length: > 0 and <= 128, Password.Length: > 0 and <= 128 } relay
           && relay.Username.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
           && relay.Password.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '=')
            ? relay
            : null;
}
