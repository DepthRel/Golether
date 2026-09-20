using Golether.Core.Identity;

namespace Golether.Session;

/// <summary>
/// An entry of the event feed.
/// </summary>
/// <param name="Time">The local time of the event.</param>
/// <param name="Actor">The participant, or <see langword="null"/> for system events.</param>
/// <param name="Text">The text.</param>
public sealed record SessionEvent(DateTimeOffset Time, PeerId? Actor, string Text);
