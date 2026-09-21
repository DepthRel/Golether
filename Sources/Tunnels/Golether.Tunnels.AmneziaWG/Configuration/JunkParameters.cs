using System.Security.Cryptography;
using Golether.Localization;

namespace Golether.Tunnels.AmneziaWG.Configuration;

/// <summary>
/// Junk packet parameters each side chooses on its own.
/// </summary>
/// <param name="Jc">The number of junk packets before a handshake.</param>
/// <param name="Jmin">The minimal junk packet size.</param>
/// <param name="Jmax">The maximal junk packet size.</param>
public sealed record JunkParameters(int Jc, int Jmin, int Jmax)
{
    /// <summary>
    /// The maximum junk packet size.
    /// </summary>
    public const int MaxJunkSize = 1280;

    /// <summary>
    /// Generates random parameters in the range recommended by AmneziaWG.
    /// </summary>
    /// <returns>The parameters.</returns>
    public static JunkParameters Generate()
    {
        var jmin = RandomNumberGenerator.GetInt32(40, 90);
        var jmax = RandomNumberGenerator.GetInt32(jmin + 100, 1000);
        return new JunkParameters(RandomNumberGenerator.GetInt32(3, 9), jmin, jmax);
    }

    /// <summary>
    /// Validates the parameters.
    /// </summary>
    /// <exception cref="FormatException">A parameter violates the AmneziaWG rules.</exception>
    public void Validate()
    {
        if (Jc is < 0 or > 128)
        {
            throw new FormatException(Texts.Get("Config.Error.JunkCount"));
        }

        if (Jc > 0 && (Jmin < 0 || Jmin >= Jmax || Jmax > MaxJunkSize))
        {
            throw new FormatException(Texts.Format("Config.Error.JunkSizes", MaxJunkSize));
        }
    }
}
