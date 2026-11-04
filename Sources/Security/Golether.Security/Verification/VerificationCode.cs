using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Golether.Core.Identity;
using Golether.Localization;

namespace Golether.Security.Verification;

/// <summary>
/// A short authentication string that two people compare by voice to rule out a man-in-the-middle: three words and a
/// number derived from both device identifiers and a context (invite token or tunnel offer).
/// </summary>
/// <remarks>
/// The code itself is three word numbers and a number, so it is the same on both sides whatever their languages. The
/// words are shown in the language of the interface: every language pack names the same 64 things
/// (<c>Verification.Word.00</c> … <c>Verification.Word.63</c>), so two people with different interface languages see
/// translations of each other's words.
/// </remarks>
/// <param name="WordNumbers">The numbers of the three words, each from 0 to <see cref="WORD_COUNT"/> − 1.</param>
/// <param name="Number">A number from 0 to 99.</param>
public sealed record VerificationCode(IReadOnlyList<int> WordNumbers, int Number)
{
    /// <summary>
    /// The number of words in the list (6 bits per word).
    /// </summary>
    public const int WORD_COUNT = 64;

    /// <summary>
    /// The prefix of the localization keys of the words; the two-digit word number follows it.
    /// </summary>
    public const string WORD_KEY_PREFIX = "Verification.Word.";

    /// <summary>
    /// Gets the three words in the language of the interface.
    /// </summary>
    public IReadOnlyList<string> Words => [.. WordNumbers.Select(WordAt)];

    /// <summary>
    /// Returns a word of the list in the language of the interface.
    /// </summary>
    /// <param name="number">The word number, from 0 to <see cref="WORD_COUNT"/> − 1.</param>
    /// <returns>The word.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The number is outside the list.</exception>
    public static string WordAt(int number)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(number);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(number, WORD_COUNT);

        return Texts.Get(WORD_KEY_PREFIX + number.ToString("00", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// The number of different codes: 64³ word combinations times 100 numbers. A man in the middle who cannot search
    /// for a match has one chance out of this many.
    /// </summary>
    public const int CODE_SPACE = WORD_COUNT * WORD_COUNT * WORD_COUNT * 100;

    /// <summary>
    /// Computes the code of a tunnel package (version 1). The result does not depend on the order of the identifiers.
    /// </summary>
    /// <remarks>
    /// Everything the code is made of is known to whoever passes the packages along, so a man in the middle can
    /// search for keys that give both sides the same code. A session uses <see cref="ComputeForSession"/>.
    /// </remarks>
    /// <param name="first">One device identifier.</param>
    /// <param name="second">The other device identifier.</param>
    /// <param name="context">The shared context, for example the identifier of a tunnel offer.</param>
    /// <returns>The code.</returns>
    public static VerificationCode Compute(PeerId first, PeerId second, string context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var (low, high) = Order(first, second);
        return FromHash(SHA256.HashData(Encoding.UTF8.GetBytes($"golether-sas-v1|{low.Value}|{high.Value}|{context}")));
    }

    /// <summary>
    /// Computes the code of an entry into a session (version 2): from both device identifiers, the context and the
    /// random numbers of both sides, the participant's committed to before the host named its own (see
    /// <see cref="SasCommitment"/>). The result does not depend on the order of the identifiers.
    /// </summary>
    /// <param name="first">One device identifier.</param>
    /// <param name="second">The other device identifier.</param>
    /// <param name="context">The token the participant presented: the invitation token or the ticket.</param>
    /// <param name="participantNonce">The number of the participant.</param>
    /// <param name="hostNonce">The number of the host.</param>
    /// <returns>The code.</returns>
    /// <exception cref="ArgumentException">A number is malformed.</exception>
    public static VerificationCode ComputeForSession(PeerId first, PeerId second, string context, string participantNonce, string hostNonce)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!SasCommitment.IsWellFormed(participantNonce) || !SasCommitment.IsWellFormed(hostNonce))
        {
            throw new ArgumentException("The numbers of the sides must be 64 lowercase hexadecimal digits.");
        }

        var (low, high) = Order(first, second);
        return FromHash(SHA256.HashData(Encoding.UTF8.GetBytes($"golether-sas-v2|{low.Value}|{high.Value}|{context}|{participantNonce}|{hostNonce}")));
    }

    /// <summary>
    /// Formats the code in the language of the interface, for example <c>owl · lighthouse · wind · 47</c>.
    /// </summary>
    /// <returns>The display text.</returns>
    public override string ToString() => $"{string.Join(" · ", Words)} · {Number:00}";

    /// <summary>
    /// Puts two identifiers in ordinal order, so both sides compute the same code.
    /// </summary>
    /// <param name="first">One identifier.</param>
    /// <param name="second">The other identifier.</param>
    /// <returns>The lower and the higher identifier.</returns>
    private static (PeerId Low, PeerId High) Order(PeerId first, PeerId second)
        => string.CompareOrdinal(first.Value, second.Value) <= 0 ? (first, second) : (second, first);

    /// <summary>
    /// Cuts a code out of a hash: three words from its first 24 bits, the number from the next two bytes.
    /// </summary>
    /// <param name="hash">The SHA-256 hash.</param>
    /// <returns>The code.</returns>
    private static VerificationCode FromHash(byte[] hash)
    {
        var bits = ((uint)hash[0] << 16) | ((uint)hash[1] << 8) | hash[2];
        int[] words =
        [
            (int)((bits >> 18) & 0x3F),
            (int)((bits >> 12) & 0x3F),
            (int)((bits >> 6) & 0x3F),
        ];
        // Little-endian by the protocol, whatever the byte order of this machine.
        var number = BinaryPrimitives.ReadUInt16LittleEndian(hash.AsSpan(3)) % 100;

        return new VerificationCode(words, number);
    }
}
