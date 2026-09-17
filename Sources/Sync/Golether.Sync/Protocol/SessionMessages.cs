using System.Text.Json.Serialization;
using Golether.Core.Identity;
using Golether.Core.Media;
using Golether.Core.Playback;
using Golether.Core.Session;
using Golether.Transports.Relay;

namespace Golether.Sync.Protocol;

/// <summary>
/// A message of the session control stream. Messages are JSON objects with the <c>$t</c> type discriminator.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$t")]
[JsonDerivedType(typeof(HelloMessage), "hello")]
[JsonDerivedType(typeof(PendingApprovalMessage), "pending")]
[JsonDerivedType(typeof(WelcomeMessage), "welcome")]
[JsonDerivedType(typeof(RejectedMessage), "rejected")]
[JsonDerivedType(typeof(ClockPingMessage), "ping")]
[JsonDerivedType(typeof(ClockPongMessage), "pong")]
[JsonDerivedType(typeof(PlaybackRequestMessage), "request")]
[JsonDerivedType(typeof(PlaybackStateMessage), "state")]
[JsonDerivedType(typeof(StatusReportMessage), "status")]
[JsonDerivedType(typeof(ParticipantsMessage), "participants")]
[JsonDerivedType(typeof(MediaChangedMessage), "media")]
[JsonDerivedType(typeof(ByeMessage), "bye")]
[JsonDerivedType(typeof(ConferenceSignalMessage), "rtc")]
[JsonDerivedType(typeof(ModerationMessage), "moderate")]
public abstract record SessionMessage
{
    /// <summary>
    /// The protocol version implemented by this build.
    /// </summary>
    public const int ProtocolVersion = 1;
}

/// <summary>
/// The first message of a participant.
/// </summary>
/// <param name="Version">The protocol version of the participant.</param>
/// <param name="DisplayName">The participant name.</param>
/// <param name="InviteToken">The invitation token; <see langword="null"/> when rejoining as a known participant.</param>
public sealed record HelloMessage(int Version, string DisplayName, string? InviteToken) : SessionMessage;

/// <summary>
/// The host received the hello and waits for the host user to approve the participant.
/// </summary>
public sealed record PendingApprovalMessage : SessionMessage;

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

/// <summary>
/// The reason a participant was not admitted.
/// </summary>
public enum RejectReason
{
    /// <summary>
    /// The invitation token is unknown, used or expired.
    /// </summary>
    InvalidInvite = 0,

    /// <summary>
    /// The host rejected the participant.
    /// </summary>
    Declined = 1,

    /// <summary>
    /// The host did not answer in time.
    /// </summary>
    ApprovalTimedOut = 2,

    /// <summary>
    /// The session is full.
    /// </summary>
    SessionFull = 3,

    /// <summary>
    /// The protocol versions are incompatible.
    /// </summary>
    IncompatibleVersion = 4,

    /// <summary>
    /// The host removed the participant.
    /// </summary>
    Removed = 5,
}

/// <summary>
/// The participant was not admitted; the host closes the stream after this message.
/// </summary>
/// <param name="Reason">The reason.</param>
/// <param name="Message">A human-readable explanation.</param>
public sealed record RejectedMessage(RejectReason Reason, string Message) : SessionMessage;

/// <summary>
/// A clock synchronization probe sent by a participant.
/// </summary>
/// <param name="ClientSendTime">The participant monotonic time when sending (t0).</param>
public sealed record ClockPingMessage(long ClientSendTime) : SessionMessage;

/// <summary>
/// The host answer to <see cref="ClockPingMessage"/>.
/// </summary>
/// <param name="ClientSendTime">The echoed participant time (t0).</param>
/// <param name="HostReceiveTime">The host time on receipt (t1).</param>
/// <param name="HostSendTime">The host time when answering (t2).</param>
public sealed record ClockPongMessage(long ClientSendTime, long HostReceiveTime, long HostSendTime) : SessionMessage;

/// <summary>
/// The kind of a playback request.
/// </summary>
public enum PlaybackRequestKind
{
    /// <summary>
    /// Start or resume playback.
    /// </summary>
    Play = 0,

    /// <summary>
    /// Pause playback.
    /// </summary>
    Pause = 1,

    /// <summary>
    /// Change the position.
    /// </summary>
    Seek = 2,
}

/// <summary>
/// A playback intent of a participant.
/// </summary>
/// <param name="Kind">The intent.</param>
/// <param name="Position">The position: the paused frame, the seek target, or the start position; <see langword="null"/>
/// to use the current session position.</param>
public sealed record PlaybackRequest(PlaybackRequestKind Kind, TimeSpan? Position);

/// <summary>
/// A participant asks the host to change the playback state. Every participant may send it.
/// </summary>
/// <param name="Request">The request.</param>
public sealed record PlaybackRequestMessage(PlaybackRequest Request) : SessionMessage;

/// <summary>
/// The authoritative playback state broadcast by the host.
/// </summary>
/// <param name="State">The state.</param>
public sealed record PlaybackStateMessage(PlaybackState State) : SessionMessage;

/// <summary>
/// The periodic status of a participant. The host replaces the peer identifier with the authenticated one.
/// </summary>
/// <param name="Status">The status.</param>
public sealed record StatusReportMessage(ParticipantStatus Status) : SessionMessage;

/// <summary>
/// The participant list and statuses broadcast by the host.
/// </summary>
/// <param name="Participants">The participants, host included.</param>
/// <param name="Statuses">The latest statuses.</param>
public sealed record ParticipantsMessage(IReadOnlyList<ParticipantInfo> Participants, IReadOnlyList<ParticipantStatus> Statuses) : SessionMessage;

/// <summary>
/// The host shares another media file (or stops sharing).
/// </summary>
/// <param name="Media">The media, or <see langword="null"/>.</param>
public sealed record MediaChangedMessage(MediaDescriptor? Media) : SessionMessage;

/// <summary>
/// WebRTC signaling for cameras and voices (SDP or ICE candidate). A participant sends it with the target in
/// <paramref name="Peer"/>; the host forwards it with the authenticated source in <paramref name="Peer"/>.
/// </summary>
/// <param name="Peer">The target (to the host) or the source (from the host).</param>
/// <param name="Kind">The kind: <c>offer</c>, <c>answer</c> or <c>candidate</c>.</param>
/// <param name="Payload">The payload.</param>
public sealed record ConferenceSignalMessage(PeerId Peer, string Kind, string Payload) : SessionMessage;

/// <summary>
/// The host switches devices of a participant off. Only switching off is possible: the participant decides whether
/// to switch them on again.
/// </summary>
/// <param name="Microphone">Whether the microphone is switched off.</param>
/// <param name="Camera">Whether the camera is switched off.</param>
public sealed record ModerationMessage(bool Microphone, bool Camera) : SessionMessage;

/// <summary>
/// The sender leaves the session.
/// </summary>
/// <param name="Reason">An optional explanation.</param>
public sealed record ByeMessage(string? Reason) : SessionMessage;
