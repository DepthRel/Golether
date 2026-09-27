namespace Golether.Transports.Rendezvous;

/// <summary>
/// Settings of the devices that meet through a rendezvous server.
/// </summary>
public sealed record RendezvousClientOptions
{
    /// <summary>
    /// Gets the time allowed to reach the server and get its greeting (default 10 s).
    /// </summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets the time a participant waits for the host to answer through the server (default 25 s; the server gives
    /// the host 20 s).
    /// </summary>
    public TimeSpan AnswerTimeout { get; init; } = TimeSpan.FromSeconds(25);

    /// <summary>
    /// Gets the time both sides keep connecting to each other (default 15 s).
    /// </summary>
    public TimeSpan PunchTimeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Gets the interval of the host's keepalive on the registration connection (default 20 s; well within the
    /// mapping lifetime of routers and the idle limit of the server).
    /// </summary>
    public TimeSpan KeepAliveInterval { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Gets the waits before the host registers again after losing the server; the last one repeats.
    /// </summary>
    public IReadOnlyList<TimeSpan> RetryDelays { get; init; } =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(60),
    ];

    /// <summary>
    /// Gets the number of meetings the host answers at once (default 8; a participant opens up to five streams).
    /// </summary>
    public int MaxConcurrentAnswers { get; init; } = 8;

    /// <summary>
    /// Validates the options.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A value is out of range.</exception>
    public void Validate()
    {
        foreach (var (value, name) in new[]
                 {
                     (ConnectTimeout, nameof(ConnectTimeout)),
                     (AnswerTimeout, nameof(AnswerTimeout)),
                     (PunchTimeout, nameof(PunchTimeout)),
                     (KeepAliveInterval, nameof(KeepAliveInterval)),
                 })
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(name, "The time must be positive.");
            }
        }

        if (RetryDelays is not { Count: > 0 } || RetryDelays.Any(d => d < TimeSpan.Zero))
        {
            throw new ArgumentOutOfRangeException(nameof(RetryDelays), "At least one non-negative delay is needed.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(MaxConcurrentAnswers, 1);
    }
}
