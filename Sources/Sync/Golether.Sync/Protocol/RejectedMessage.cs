using Golether.Core.Data.Enums;

namespace Golether.Sync.Protocol;

/// <summary>
/// The participant was not admitted; the host closes the stream after this message.
/// </summary>
/// <param name="Reason">The reason.</param>
/// <param name="Message">A short note for logs. The receiver does not show it: the text for the user is chosen by
/// <paramref name="Reason"/> in the language of the receiver, so the host and the participant may use different
/// languages.</param>
public sealed record RejectedMessage(RejectReason Reason, string Message) : SessionMessage;
