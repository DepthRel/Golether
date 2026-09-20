using Golether.Core.Playback;

namespace Golether.Sync.Engine;

/// <summary>
/// The result of a follower tick, used for status reports and the UI.
/// </summary>
/// <param name="Snapshot">The player snapshot.</param>
/// <param name="Expected">The session position, or <see langword="null"/> without state.</param>
/// <param name="Drift">The drift from the session position: positive when ahead.</param>
/// <param name="StartsIn">The time until a scheduled start, or <see langword="null"/>.</param>
public readonly record struct FollowerStatus(PlayerSnapshot Snapshot, TimeSpan? Expected, TimeSpan Drift, TimeSpan? StartsIn);
