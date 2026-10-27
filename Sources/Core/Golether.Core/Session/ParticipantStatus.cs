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

    /// <summary>
    /// Gets the picture the participant looks at alone, away from the one everybody sees, or <see langword="null"/>
    /// while it follows the common screen.
    /// </summary>
    public int? GalleryView { get; init; }

    /// <summary>
    /// The largest time accepted in a status that came from the network. Larger values (up to the limits of
    /// <see cref="TimeSpan"/>) would overflow as soon as they are compared, negated or subtracted.
    /// </summary>
    public static readonly TimeSpan MaxSpan = TimeSpan.FromDays(30);

    /// <summary>
    /// The largest round trip accepted from the network, in milliseconds.
    /// </summary>
    public const int MAX_ROUND_TRIP_MILLISECONDS = 600_000;

    /// <summary>
    /// Returns the status with every number brought into a sane range. A status comes from another device and is
    /// used in arithmetic by the host and shown by everybody, so it is never taken as it is.
    /// </summary>
    /// <returns>The status with bounded values.</returns>
    public ParticipantStatus Sanitize() => this with
    {
        Position = Position is { } position ? Bound(position) : null,
        Drift = Bound(Drift),
        CacheAhead = Bound(CacheAhead),
        RoundTripMilliseconds = RoundTripMilliseconds is { } roundTrip ? Math.Clamp(roundTrip, 0, MAX_ROUND_TRIP_MILLISECONDS) : null,
        BytesFromPeers = Math.Max(0, BytesFromPeers),
        BytesToPeers = Math.Max(0, BytesToPeers),
        GalleryView = GalleryView is >= 0 and < Media.GalleryItem.MAX_ITEMS ? GalleryView : null,
    };

    /// <summary>
    /// Brings a time into the range from minus to plus <see cref="MaxSpan"/>.
    /// </summary>
    /// <param name="value">The time.</param>
    /// <returns>The bounded time.</returns>
    private static TimeSpan Bound(TimeSpan value) => value < -MaxSpan ? -MaxSpan : value > MaxSpan ? MaxSpan : value;
}
