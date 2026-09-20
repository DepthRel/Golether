using Golether.Core.Data.Enums;

namespace Golether.Sync.Protocol;

/// <summary>
/// The participant was not admitted; the host closes the stream after this message.
/// </summary>
/// <param name="Reason">The reason.</param>
/// <param name="Message">A human-readable explanation.</param>
public sealed record RejectedMessage(RejectReason Reason, string Message) : SessionMessage;
