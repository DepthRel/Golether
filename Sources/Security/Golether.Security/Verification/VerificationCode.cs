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
    /// Computes the code. The result does not depend on the order of the identifiers.
    /// </summary>
    /// <param name="first">One device identifier.</param>
    /// <param name="second">The other device identifier.</param>
    /// <param name="context">The shared context, for example the invitation token.</param>
    /// <returns>The code.</returns>
    public static VerificationCode Compute(PeerId first, PeerId second, string context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var (low, high) = string.CompareOrdinal(first.Value, second.Value) <= 0 ? (first, second) : (second, first);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"golether-sas-v1|{low.Value}|{high.Value}|{context}"));

        var bits = ((uint)hash[0] << 16) | ((uint)hash[1] << 8) | hash[2];
        int[] words =
        [
            (int)((bits >> 18) & 0x3F),
            (int)((bits >> 12) & 0x3F),
            (int)((bits >> 6) & 0x3F),
        ];
        var number = BitConverter.ToUInt16(hash, 3) % 100;

        return new VerificationCode(words, number);
    }

    /// <summary>
    /// Formats the code in the language of the interface, for example <c>owl · lighthouse · wind · 47</c>.
    /// </summary>
    /// <returns>The display text.</returns>
    public override string ToString() => $"{string.Join(" · ", Words)} · {Number:00}";
}
