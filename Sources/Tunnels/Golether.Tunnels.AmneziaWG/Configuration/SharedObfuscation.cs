using System.Security.Cryptography;
using Golether.Localization;

namespace Golether.Tunnels.AmneziaWG.Configuration;

/// <summary>
/// Obfuscation parameters that both sides of an AmneziaWG tunnel must share (format of AmneziaWG 1.x).
/// </summary>
/// <param name="S1">The junk prefix length of the handshake initiation.</param>
/// <param name="S2">The junk prefix length of the handshake response.</param>
/// <param name="H1">The header of the handshake initiation.</param>
/// <param name="H2">The header of the handshake response.</param>
/// <param name="H3">The header of the cookie reply.</param>
/// <param name="H4">The header of transport data.</param>
public sealed record SharedObfuscation(int S1, int S2, uint H1, uint H2, uint H3, uint H4)
{
    /// <summary>
    /// The maximum S1 (1280 − 148, the size of a handshake initiation).
    /// </summary>
    public const int MaxS1 = 1132;

    /// <summary>
    /// The maximum S2 (1280 − 92, the size of a handshake response).
    /// </summary>
    public const int MaxS2 = 1188;

    /// <summary>
    /// The smallest header value that differs from plain WireGuard message types (1–4).
    /// </summary>
    public const uint MinHeader = 5;

    /// <summary>
    /// Generates random parameters.
    /// </summary>
    /// <returns>The parameters.</returns>
    public static SharedObfuscation Generate()
    {
        var s1 = RandomNumberGenerator.GetInt32(15, 151);
        int s2;
        do
        {
            s2 = RandomNumberGenerator.GetInt32(15, 151);
        }
        while (s1 + 56 == s2);

        var headers = new HashSet<uint>();
        while (headers.Count < 4)
        {
            headers.Add((uint)RandomNumberGenerator.GetInt32((int)MinHeader, int.MaxValue));
        }

        var h = headers.ToArray();
        return new SharedObfuscation(s1, s2, h[0], h[1], h[2], h[3]);
    }

    /// <summary>
    /// Validates the parameters.
    /// </summary>
    /// <exception cref="FormatException">A parameter violates the AmneziaWG rules.</exception>
    public void Validate()
    {
        if (S1 is < 0 or > MaxS1 || S2 is < 0 or > MaxS2)
        {
            throw new FormatException(Texts.Format("Config.Error.PaddingSizes", MaxS1, MaxS2));
        }

        if (S1 + 56 == S2)
        {
            throw new FormatException(Texts.Get("Config.Error.PaddingEqual"));
        }

        var headers = new[] { H1, H2, H3, H4 };
        if (headers.Distinct().Count() != 4 || headers.Any(h => h < MinHeader))
        {
            throw new FormatException(Texts.Get("Config.Error.Headers"));
        }
    }
}
