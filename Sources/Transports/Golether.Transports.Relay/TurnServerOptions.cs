using System.Net;

namespace Golether.Transports.Relay;

/// <summary>
/// Settings of <see cref="TurnServer"/>.
/// </summary>
public sealed record TurnServerOptions
{
    /// <summary>
    /// Gets the credentials.
    /// </summary>
    public required RelayCredentials Credentials { get; init; }

    /// <summary>
    /// Gets the address to listen on.
    /// </summary>
    public IPAddress ListenAddress { get; init; } = IPAddress.IPv6Any;

    /// <summary>
    /// Gets the realm of the long-term credentials.
    /// </summary>
    public string Realm { get; init; } = "golether";

    /// <summary>
    /// Gets the maximum number of simultaneous allocations: a session has at most four participants, each with a
    /// connection to every other one and the ones being made again (the session gives each participant 16 at most).
    /// </summary>
    public int MaxAllocations { get; init; } = 64;

    /// <summary>
    /// Gets the longest allocation lifetime granted.
    /// </summary>
    public TimeSpan MaxLifetime { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Gets the time a connection may stay without an allocation.
    /// </summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
