using System.Security.Cryptography;

namespace Golether.Transports.Relay;

/// <summary>
/// Credentials of the relay, handed only to admitted participants over the session channel.
/// </summary>
/// <param name="Port">The TCP port of the relay.</param>
/// <param name="Username">The user name.</param>
/// <param name="Password">The password.</param>
public sealed record RelayCredentials(int Port, string Username, string Password)
{
    /// <summary>
    /// Creates random credentials.
    /// </summary>
    /// <param name="port">The TCP port of the relay.</param>
    /// <returns>The credentials.</returns>
    public static RelayCredentials CreateRandom(int port)
        => new(port, "golether-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant(),
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_'));

    /// <summary>
    /// Builds the TURN address for a relay host, for example <c>turn://user:pass@203.0.113.7:47801?transport=tcp</c>.
    /// </summary>
    /// <param name="host">The address of the host as the participant reaches it.</param>
    /// <returns>The address.</returns>
    public string ToTurnUri(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        var address = host.Contains(':', StringComparison.Ordinal) && !host.StartsWith('[') ? $"[{host}]" : host;
        return $"turn://{Uri.EscapeDataString(Username)}:{Uri.EscapeDataString(Password)}@{address}:{Port}?transport=tcp";
    }
}
