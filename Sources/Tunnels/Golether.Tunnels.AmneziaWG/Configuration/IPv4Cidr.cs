using System.Globalization;
using System.Net;

namespace Golether.Tunnels.AmneziaWG.Configuration;

/// <summary>
/// An IPv4 address with a prefix length.
/// </summary>
/// <param name="Address">The address.</param>
/// <param name="PrefixLength">The prefix length, 0–32.</param>
public readonly record struct IPv4Cidr(IPAddress Address, int PrefixLength)
{
    /// <summary>
    /// Parses <c>a.b.c.d/n</c>.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="value">The parsed value.</param>
    /// <returns><see langword="true"/> when valid.</returns>
    public static bool TryParse(string? text, out IPv4Cidr value)
    {
        value = default;
        var slash = text?.IndexOf('/') ?? -1;
        if (slash <= 0
            || !IPAddress.TryParse(text![..slash], out var address)
            || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork
            || text[..slash].Count(c => c == '.') != 3
            || !int.TryParse(text[(slash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var prefix)
            || prefix > 32)
        {
            return false;
        }

        value = new IPv4Cidr(address, prefix);
        return true;
    }

    /// <summary>
    /// Formats the value as <c>a.b.c.d/n</c>.
    /// </summary>
    /// <returns>The text.</returns>
    public override string ToString() => $"{Address}/{PrefixLength.ToString(CultureInfo.InvariantCulture)}";
}
