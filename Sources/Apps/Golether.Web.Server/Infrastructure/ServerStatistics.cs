using System.Reflection;
using Golether.Transports.Rendezvous.Protocol;

namespace Golether.Web.Server.Infrastructure;

/// <summary>
/// Counters of the server for the health page. Nothing about the devices is kept: only numbers.
/// </summary>
internal sealed class ServerStatistics
{
    /// <summary>
    /// The time provider.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// When the server started.
    /// </summary>
    private readonly DateTimeOffset _started;

    /// <summary>
    /// The open TCP connections.
    /// </summary>
    private long _connections;

    /// <summary>
    /// The meetings a participant asked for.
    /// </summary>
    private long _meetingsRequested;

    /// <summary>
    /// The meetings where both sides got each other's address.
    /// </summary>
    private long _meetingsIntroduced;

    /// <summary>
    /// The connections and requests refused for exceeding a limit.
    /// </summary>
    private long _refused;

    /// <summary>
    /// The STUN requests answered.
    /// </summary>
    private long _stunAnswered;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerStatistics"/> class.
    /// </summary>
    /// <param name="timeProvider">The time provider.</param>
    public ServerStatistics(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _started = timeProvider.GetUtcNow();
    }

    /// <summary>
    /// Gets or sets a function that counts the held rooms.
    /// </summary>
    public Func<int> CountRooms { get; set; } = () => 0;

    /// <summary>
    /// Counts an opened connection.
    /// </summary>
    public void ConnectionOpened() => Interlocked.Increment(ref _connections);

    /// <summary>
    /// Counts a closed connection.
    /// </summary>
    public void ConnectionClosed() => Interlocked.Decrement(ref _connections);

    /// <summary>
    /// Counts a meeting a participant asked for.
    /// </summary>
    public void MeetingRequested() => Interlocked.Increment(ref _meetingsRequested);

    /// <summary>
    /// Counts a meeting where both sides got each other's address.
    /// </summary>
    public void MeetingIntroduced() => Interlocked.Increment(ref _meetingsIntroduced);

    /// <summary>
    /// Counts a refusal for exceeding a limit.
    /// </summary>
    public void Refused() => Interlocked.Increment(ref _refused);

    /// <summary>
    /// Counts an answered STUN request.
    /// </summary>
    public void StunAnswered() => Interlocked.Increment(ref _stunAnswered);

    /// <summary>
    /// Takes the current values.
    /// </summary>
    /// <returns>The snapshot.</returns>
    public HealthReport Snapshot() => new(
        "ok",
        typeof(ServerStatistics).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown",
        RendezvousProtocol.Version,
        (long)(_timeProvider.GetUtcNow() - _started).TotalSeconds,
        CountRooms(),
        Interlocked.Read(ref _connections),
        Interlocked.Read(ref _meetingsRequested),
        Interlocked.Read(ref _meetingsIntroduced),
        Interlocked.Read(ref _refused),
        Interlocked.Read(ref _stunAnswered));
}
