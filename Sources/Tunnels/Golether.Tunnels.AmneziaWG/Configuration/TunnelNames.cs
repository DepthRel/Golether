namespace Golether.Tunnels.AmneziaWG.Configuration;

/// <summary>
/// Validation of tunnel interface names.
/// </summary>
public static class TunnelNames
{
    /// <summary>
    /// Checks an interface name: 1–15 characters from <c>A–Z a–z 0–9 _ = + . -</c> (the rule of <c>wg-quick</c>).
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns><see langword="true"/> when valid.</returns>
    public static bool IsValid(string? name)
        => name is { Length: >= 1 and <= 15 }
           && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '=' or '+' or '.' or '-')
           && name is not ("." or "..");

    /// <summary>
    /// Throws when a name is invalid.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <exception cref="ArgumentException">The name is invalid.</exception>
    public static void Validate(string? name)
    {
        if (!IsValid(name))
        {
            throw new ArgumentException("The tunnel name must have 1–15 characters: letters, digits, _ = + . -", nameof(name));
        }
    }
}
