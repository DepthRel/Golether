using Golether.Core.Identity;
using Golether.Core.Media;
using Golether.Core.Playback;
using Golether.Security.Verification;
using Golether.Sync.Engine;

namespace Golether.Session;

/// <summary>
/// A consistent view of a session for the UI.
/// </summary>
public sealed record SessionSnapshot
{
    /// <summary>
    /// Gets a value indicating whether this device hosts the session.
    /// </summary>
    public required bool IsHost { get; init; }

    /// <summary>
    /// Gets the connection state.
    /// </summary>
    public required SessionState State { get; init; }

    /// <summary>
    /// Gets the session name.
    /// </summary>
    public required string SessionName { get; init; }

    /// <summary>
    /// Gets the host identifier.
    /// </summary>
    public required PeerId HostPeerId { get; init; }

    /// <summary>
    /// Gets the participants, this device included.
    /// </summary>
    public required IReadOnlyList<ParticipantView> Participants { get; init; }

    /// <summary>
    /// Gets the playback state, or <see langword="null"/> before the first state.
    /// </summary>
    public PlaybackState? Playback { get; init; }

    /// <summary>
    /// Gets the shared media, or <see langword="null"/>.
    /// </summary>
    public MediaDescriptor? Media { get; init; }

    /// <summary>
    /// Gets the local follower status.
    /// </summary>
    public FollowerStatus? Local { get; init; }

    /// <summary>
    /// Gets the round trip to the host (participants only).
    /// </summary>
    public TimeSpan? RoundTrip { get; init; }

    /// <summary>
    /// Gets the clock uncertainty (participants only).
    /// </summary>
    public TimeSpan? ClockUncertainty { get; init; }

    /// <summary>
    /// Gets the code to compare with the host (participants only).
    /// </summary>
    public VerificationCode? VerificationCode { get; init; }

    /// <summary>
    /// Gets a value indicating whether this participant plays a local copy.
    /// </summary>
    public bool UsesLocalCopy { get; init; }
}
