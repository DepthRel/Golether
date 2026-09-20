using Golether.Core.Identity;

namespace Golether.Core.Session;

/// <summary>
/// The playback status a participant reports to the host periodically.
/// </summary>
public sealed record ParticipantStatus
{
    /// <summary>
    /// Gets the participant.
    /// </summary>
    public required PeerId PeerId { get; init; }

    /// <summary>
    /// Gets the local player position, or <see langword="null"/> when no media is loaded.
    /// </summary>
    public TimeSpan? Position { get; init; }

    /// <summary>
    /// Gets the drift from the session position: positive when the participant is ahead.
    /// </summary>
    public TimeSpan Drift { get; init; }

    /// <summary>
    /// Gets the amount of media buffered ahead of the position.
    /// </summary>
    public TimeSpan CacheAhead { get; init; }

    /// <summary>
    /// Gets a value indicating whether the player waits for data.
    /// </summary>
    public bool IsBuffering { get; init; }

    /// <summary>
    /// Gets the round-trip time to the host in milliseconds, or <see langword="null"/> while unknown.
    /// </summary>
    public int? RoundTripMilliseconds { get; init; }

    /// <summary>
    /// Gets a value indicating whether the participant plays the media from a local copy.
    /// </summary>
    public bool UsesLocalCopy { get; init; }

    /// <summary>
    /// Gets a value indicating whether the microphone of the participant is muted.
    /// </summary>
    public bool MicrophoneOff { get; init; }

    /// <summary>
    /// Gets a value indicating whether the camera of the participant is off.
    /// </summary>
    public bool CameraOff { get; init; }

    /// <summary>
    /// Gets the checked file bytes the participant received from other participants instead of the host.
    /// </summary>
    public long BytesFromPeers { get; init; }

    /// <summary>
    /// Gets the file bytes the participant sent to other participants.
    /// </summary>
    public long BytesToPeers { get; init; }
}
