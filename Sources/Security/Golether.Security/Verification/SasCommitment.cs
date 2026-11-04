using System.Security.Cryptography;

namespace Golether.Security.Verification;

/// <summary>
/// The random numbers both sides add to the verification code, and the commitment that keeps a side from choosing its
/// number after it saw the other one.
/// </summary>
/// <remarks>
/// The participant commits to its number in the hello (the SHA-256 of it), the host then names its own number, and
/// only after that the participant reveals its number. A man in the middle has to fix his choice on each side before
/// he learns the number of the honest side there, so he cannot search for keys that give both sides the same code:
/// every attempt is one guess out of <see cref="VerificationCode.CODE_SPACE"/>.
/// </remarks>
public static class SasCommitment
{
    /// <summary>
    /// The number of random bytes in a number of a side.
    /// </summary>
    public const int NONCE_BYTES = 32;

    /// <summary>
    /// The length of a number or a commitment as lowercase hexadecimal text.
    /// </summary>
    public const int TEXT_LENGTH = 64;

    /// <summary>
    /// Creates the random number of a side.
    /// </summary>
    /// <returns>The number as lowercase hexadecimal text.</returns>
    public static string CreateNonce() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(NONCE_BYTES));

    /// <summary>
    /// Makes the commitment to a number: what the participant sends before it knows the number of the host.
    /// </summary>
    /// <param name="nonce">The number, as <see cref="CreateNonce"/> made it.</param>
    /// <returns>The lowercase hexadecimal SHA-256 of the bytes of the number.</returns>
    /// <exception cref="ArgumentException">The number is malformed.</exception>
    public static string Commit(string nonce)
    {
        if (!IsWellFormed(nonce))
        {
            throw new ArgumentException("The number must be 64 lowercase hexadecimal digits.", nameof(nonce));
        }

        return Convert.ToHexStringLower(SHA256.HashData(Convert.FromHexString(nonce)));
    }

    /// <summary>
    /// Tells whether a number or a commitment that came over the network has the right form.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns><see langword="true"/> for 64 lowercase hexadecimal digits.</returns>
    public static bool IsWellFormed(string? text)
        => text is { Length: TEXT_LENGTH } && text.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f');

    /// <summary>
    /// Checks a revealed number against the commitment made before, in constant time.
    /// </summary>
    /// <param name="commitment">The commitment from the hello.</param>
    /// <param name="nonce">The revealed number.</param>
    /// <returns><see langword="true"/> when the number is the one committed to.</returns>
    public static bool Matches(string? commitment, string? nonce)
    {
        if (!IsWellFormed(commitment) || !IsWellFormed(nonce))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(commitment!), SHA256.HashData(Convert.FromHexString(nonce!)));
    }
}
