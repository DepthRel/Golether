using System.Globalization;
using System.Text;
using Golether.Core.Identity;

namespace Golether.Sync.Protocol;

/// <summary>
/// The kind of a chat message.
/// </summary>
public enum ChatKind
{
    /// <summary>
    /// A text line.
    /// </summary>
    Text = 0,

    /// <summary>
    /// A reaction shown over the video for a moment.
    /// </summary>
    Reaction = 1,
}

/// <summary>
/// A chat line or a reaction. Participants send it to the host without <paramref name="Sender"/>; the host sets the
/// authenticated sender and relays it to everybody (including the sender), so all see the same order.
/// </summary>
/// <param name="Id">A random identifier chosen by the sender.</param>
/// <param name="Kind">The kind.</param>
/// <param name="Text">The text, or the reaction symbol.</param>
/// <param name="Sender">The sender, set by the host only.</param>
public sealed record ChatMessage(string Id, ChatKind Kind, string Text, PeerId? Sender) : SessionMessage
{
    /// <summary>
    /// The maximum length of a text line, in characters.
    /// </summary>
    public const int MaxTextLength = 500;

    /// <summary>
    /// The maximum length of an identifier.
    /// </summary>
    public const int MaxIdLength = 32;

    /// <summary>
    /// The reactions that may be sent.
    /// </summary>
    public static readonly IReadOnlyList<string> Reactions = ["👍", "😂", "😮", "😢", "❤️", "🔥", "👏"];

    /// <summary>
    /// Creates a message of this device.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <param name="text">The text or reaction.</param>
    /// <returns>The message, or <see langword="null"/> when the text is empty or not allowed.</returns>
    public static ChatMessage? Create(ChatKind kind, string? text)
        => Normalize(kind, text) is { } normalized
            ? new ChatMessage(Convert.ToHexString(Guid.NewGuid().ToByteArray()), kind, normalized, null)
            : null;

    /// <summary>
    /// Validates a received message and cleans its text.
    /// </summary>
    /// <returns>The cleaned message, or <see langword="null"/> when it must be dropped.</returns>
    public ChatMessage? Sanitize()
    {
        if (string.IsNullOrEmpty(Id) || Id.Length > MaxIdLength || !Id.All(char.IsAsciiHexDigit) || !Enum.IsDefined(Kind))
        {
            return null;
        }

        return Normalize(Kind, Text) is { } text ? this with { Text = text } : null;
    }

    /// <summary>
    /// Cleans a text: removes control and formatting characters (keeps line breaks), trims and limits the length;
    /// reactions must be one of <see cref="Reactions"/>.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <param name="text">The text.</param>
    /// <returns>The clean text, or <see langword="null"/> when nothing is left or the reaction is unknown.</returns>
    public static string? Normalize(ChatKind kind, string? text)
    {
        if (text is null)
        {
            return null;
        }

        if (kind == ChatKind.Reaction)
        {
            return Reactions.Contains(text, StringComparer.Ordinal) ? text : null;
        }

        if (kind != ChatKind.Text || text.Length > MaxTextLength * 4)
        {
            return null;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (rune.Value == '\n')
            {
                builder.Append('\n');
            }
            else if (rune.Value == '\t')
            {
                builder.Append(' ');
            }
            else if (category is UnicodeCategory.Control or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse
                || (category == UnicodeCategory.Format && rune.Value != 0x200D))
            {
                // Bidirectional overrides and other invisible characters could disguise the text; the zero-width
                // joiner stays for composite emoji.
                continue;
            }
            else
            {
                builder.Append(rune.ToString());
            }
        }

        var clean = builder.ToString().Replace("\r", string.Empty, StringComparison.Ordinal).Trim();
        while (clean.Contains("\n\n\n", StringComparison.Ordinal))
        {
            clean = clean.Replace("\n\n\n", "\n\n", StringComparison.Ordinal);
        }

        if (clean.Length > MaxTextLength)
        {
            var cut = MaxTextLength;
            if (char.IsHighSurrogate(clean[cut - 1]))
            {
                cut--;
            }

            clean = clean[..cut];
        }

        return clean.Length == 0 ? null : clean;
    }
}

/// <summary>
/// Limits how often a participant may send chat messages: a sliding window per kind.
/// </summary>
public sealed class ChatRateLimiter
{
    /// <summary>
    /// The window.
    /// </summary>
    private readonly TimeSpan _window;

    /// <summary>
    /// The allowed number of texts per window.
    /// </summary>
    private readonly int _texts;

    /// <summary>
    /// The allowed number of reactions per window.
    /// </summary>
    private readonly int _reactions;

    /// <summary>
    /// The recent send times of texts.
    /// </summary>
    private readonly Queue<long> _recentTexts = new();

    /// <summary>
    /// The recent send times of reactions.
    /// </summary>
    private readonly Queue<long> _recentReactions = new();

    /// <summary>
    /// Guards the queues.
    /// </summary>
    private readonly Lock _gate = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatRateLimiter"/> class.
    /// </summary>
    /// <param name="window">The window (5 s by default).</param>
    /// <param name="texts">The allowed texts per window (5 by default).</param>
    /// <param name="reactions">The allowed reactions per window (15 by default).</param>
    public ChatRateLimiter(TimeSpan? window = null, int texts = 5, int reactions = 15)
    {
        _window = window ?? TimeSpan.FromSeconds(5);
        _texts = texts;
        _reactions = reactions;
    }

    /// <summary>
    /// Checks whether a message may pass now and counts it.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <param name="timestamp">The current time in <see cref="TimeProvider.GetTimestamp"/> units.</param>
    /// <param name="timeProvider">The time provider of <paramref name="timestamp"/>.</param>
    /// <returns><see langword="true"/> when the message may pass.</returns>
    public bool TryAcquire(ChatKind kind, long timestamp, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        var (queue, limit) = kind == ChatKind.Reaction ? (_recentReactions, _reactions) : (_recentTexts, _texts);
        lock (_gate)
        {
            while (queue.Count > 0 && timeProvider.GetElapsedTime(queue.Peek(), timestamp) >= _window)
            {
                queue.Dequeue();
            }

            if (queue.Count >= limit)
            {
                return false;
            }

            queue.Enqueue(timestamp);
            return true;
        }
    }
}
