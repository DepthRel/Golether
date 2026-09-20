using Golether.Core.Data.Enums;

namespace Golether.Sync.Protocol;

/// <summary>
/// A playback intent of a participant.
/// </summary>
/// <param name="Kind">The intent.</param>
/// <param name="Position">The position: the paused frame, the seek target, or the start position; <see langword="null"/>
/// to use the current session position.</param>
public sealed record PlaybackRequest(PlaybackRequestKind Kind, TimeSpan? Position);
