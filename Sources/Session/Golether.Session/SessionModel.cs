using System.Globalization;
using Golether.Core.Identity;
using Golether.Core.Media;
using Golether.Core.Playback;
using Golether.Core.Session;
using Golether.Security.Verification;
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

/// <summary>
/// The connection state of the local side.
/// </summary>
public enum SessionState
{
    /// <summary>
    /// Connecting to the host.
    /// </summary>
    Connecting = 0,

    /// <summary>
    /// Waiting for the host to approve.
    /// </summary>
    AwaitingApproval = 1,

    /// <summary>
    /// Watching.
    /// </summary>
    Active = 2,

    /// <summary>
    /// The session ended.
    /// </summary>
    Ended = 3,
}

/// <summary>
/// A participant as shown in the UI.
/// </summary>
/// <param name="Info">The participant.</param>
/// <param name="Status">The latest status, or <see langword="null"/>.</param>
/// <param name="IsLocal">Whether the entry is this device.</param>
public sealed record ParticipantView(ParticipantInfo Info, ParticipantStatus? Status, bool IsLocal);

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

/// <summary>
/// An entry of the event feed.
/// </summary>
/// <param name="Time">The local time of the event.</param>
/// <param name="Actor">The participant, or <see langword="null"/> for system events.</param>
/// <param name="Text">The text.</param>
public sealed record SessionEvent(DateTimeOffset Time, PeerId? Actor, string Text);

/// <summary>
/// Formats event feed texts.
/// </summary>
public static class SessionTexts
{
    /// <summary>
    /// Formats a media position as <c>h:mm:ss</c>.
    /// </summary>
    /// <param name="position">The position.</param>
    /// <returns>The text.</returns>
    public static string FormatPosition(TimeSpan position)
        => ((int)position.TotalHours).ToString(CultureInfo.InvariantCulture) + position.ToString(@"\:mm\:ss", CultureInfo.InvariantCulture);

    /// <summary>
    /// Describes a playback change.
    /// </summary>
    /// <param name="state">The new state.</param>
    /// <param name="name">The name of the originator.</param>
    /// <returns>The text.</returns>
    public static string Describe(PlaybackState state, string name)
    {
        ArgumentNullException.ThrowIfNull(state);
        var position = FormatPosition(state.Position);
        return state.Cause switch
        {
            PlaybackCause.Play => $"{name} запускает воспроизведение с {position}",
            PlaybackCause.Pause => $"{name} ставит на паузу на {position}",
            PlaybackCause.Seek => $"{name} перематывает на {position}",
            PlaybackCause.WaitingForParticipants => $"Пауза на {position}: ждём участников с медленной связью",
            PlaybackCause.ParticipantsReady => "Все участники готовы, продолжаем",
            _ => "Файл готов к просмотру",
        };
    }
}

/// <summary>
/// Joining a session failed.
/// </summary>
public sealed class SessionJoinException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SessionJoinException"/> class.
    /// </summary>
    /// <param name="message">The message for the user.</param>
    /// <param name="innerException">The cause.</param>
    public SessionJoinException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
