using Golether.Core.Identity;
using Golether.Sync.Protocol;

namespace Golether.Session;

/// <summary>
/// A chat line or reaction as shown to the user.
/// </summary>
/// <param name="Id">The message identifier.</param>
/// <param name="Sender">The authenticated sender.</param>
/// <param name="SenderName">The display name of the sender.</param>
/// <param name="Kind">The kind.</param>
/// <param name="Text">The clean text or reaction.</param>
/// <param name="Time">The local receive time.</param>
/// <param name="IsLocal">Whether this device sent it.</param>
public sealed record ChatEntry(string Id, PeerId Sender, string SenderName, ChatKind Kind, string Text, DateTimeOffset Time, bool IsLocal);
