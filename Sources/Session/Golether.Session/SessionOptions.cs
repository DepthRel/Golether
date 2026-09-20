using Golether.Core.Playback;
using Golether.Sync.Engine;

namespace Golether.Session;

/// <summary>
/// Settings of watch sessions.
/// </summary>
public sealed record SessionOptions
{
    /// <summary>
    /// The largest supported number of participants, host included.
    /// </summary>
    public const int MaxParticipantsLimit = 5;

    /// <summary>
    /// Gets the maximum number of participants, host included (default and upper limit 5).
    /// </summary>
    public int MaxParticipants { get; init; } = MaxParticipantsLimit;

    /// <summary>
    /// Gets the number of parallel media streams per participant (default 4).
    /// </summary>
    public int MediaDataStreams { get; init; } = 4;

    /// <summary>
    /// Gets the interval of the synchronization loop (default 250 ms).
    /// </summary>
    public TimeSpan TickInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Gets the interval of status reports and participant broadcasts (default 1 s).
    /// </summary>
    public TimeSpan StatusInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Gets the interval of clock probes after the initial burst (default 2 s).
    /// </summary>
    public TimeSpan ClockProbeInterval { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Gets the time a peer has to send its hello (default 30 s).
    /// </summary>
    public TimeSpan HelloTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets the time a single message may take to be sent before the peer is dropped (default 10 s).
    /// </summary>
    public TimeSpan SendTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets the drift correction settings.
    /// </summary>
    public DriftCorrectionOptions DriftCorrection { get; init; } = new();

    /// <summary>
    /// Gets the authority settings of the host.
    /// </summary>
    public PlaybackAuthorityOptions Authority { get; init; } = new();

    /// <summary>
    /// Validates the options.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A value is out of range.</exception>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxParticipants, 2);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MaxParticipants, MaxParticipantsLimit);
        ArgumentOutOfRangeException.ThrowIfLessThan(MediaDataStreams, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(MediaDataStreams, 16);
        DriftCorrection.Validate();
    }
}
