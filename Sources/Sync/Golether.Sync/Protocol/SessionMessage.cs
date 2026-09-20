using System.Text.Json.Serialization;

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
[JsonDerivedType(typeof(ChatMessage), "chat")]
[JsonDerivedType(typeof(DrawMessage), "draw")]
public abstract record SessionMessage
{
    /// <summary>
    /// The protocol version implemented by this build.
    /// </summary>
    public const int ProtocolVersion = 1;
}
