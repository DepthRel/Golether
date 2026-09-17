using System.Security.Cryptography;
using System.Text;
using Golether.Core.Identity;

namespace Golether.Security.Verification;

/// <summary>
/// A short authentication string that two people compare by voice to rule out a man-in-the-middle: three words and a
/// number derived from both device identifiers and a context (invite token or tunnel offer).
/// </summary>
/// <param name="Words">Three words.</param>
/// <param name="Number">A number from 0 to 99.</param>
public sealed record VerificationCode(IReadOnlyList<string> Words, int Number)
{
    /// <summary>
    /// The word list: 64 short, distinct Russian nouns (6 bits per word).
    /// </summary>
    public static readonly IReadOnlyList<string> WordList =
    [
        "сова", "маяк", "ветер", "река", "гора", "лиса", "кедр", "мост",
        "снег", "луна", "волна", "берег", "камень", "облако", "ключ", "парус",
        "якорь", "звезда", "поле", "роса", "туман", "дождь", "гроза", "заря",
        "сокол", "медведь", "олень", "тигр", "кит", "дельфин", "ласточка", "журавль",
        "липа", "дуб", "клён", "берёза", "сосна", "ель", "вишня", "яблоко",
        "груша", "слива", "малина", "орех", "мёд", "хлеб", "соль", "чай",
        "перо", "книга", "лампа", "окно", "дверь", "крыша", "башня", "замок",
        "город", "остров", "пустыня", "озеро", "ручей", "вулкан", "компас", "фонарь",
    ];

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
        var words = new[]
        {
            WordList[(int)((bits >> 18) & 0x3F)],
            WordList[(int)((bits >> 12) & 0x3F)],
            WordList[(int)((bits >> 6) & 0x3F)],
        };
        var number = BitConverter.ToUInt16(hash, 3) % 100;
        return new VerificationCode(words, number);
    }

    /// <summary>
    /// Formats the code, for example <c>сова · маяк · ветер · 47</c>.
    /// </summary>
    /// <returns>The display text.</returns>
    public override string ToString() => $"{string.Join(" · ", Words)} · {Number:00}";
}
