using System.Globalization;
using Golether.Localization;
using Golether.Web.Server.Contracts;

namespace Golether.Web.Server.UI.ViewModels;

/// <summary>
/// One event of the journal as a row of the table, in the language of the user.
/// </summary>
public sealed class JournalRowViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="JournalRowViewModel"/> class.
    /// </summary>
    /// <param name="entry">The event.</param>
    /// <param name="columns">The widths of the columns, shared by all rows and the header.</param>
    public JournalRowViewModel(JournalEntry entry, JournalColumns columns)
    {
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));
        Columns = columns ?? throw new ArgumentNullException(nameof(columns));
        Time = entry.Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        Address = entry.Address ?? "—";
        Event = Texts.Get("Monitor.Event." + entry.Event);
        Details = DescribeDetails(entry);
        IsProblem = entry.Event is JournalEvent.HostRefused or JournalEvent.MeetingFailed or JournalEvent.ConnectionRefused
            or JournalEvent.BadRequest or JournalEvent.ConnectionFailed or JournalEvent.AcceptFailed;
    }

    /// <summary>
    /// Gets the event.
    /// </summary>
    public JournalEntry Entry { get; }

    /// <summary>
    /// Gets the widths of the columns.
    /// </summary>
    public JournalColumns Columns { get; }

    /// <summary>
    /// Gets the local time.
    /// </summary>
    public string Time { get; }

    /// <summary>
    /// Gets the address of the device, or a dash.
    /// </summary>
    public string Address { get; }

    /// <summary>
    /// Gets what happened.
    /// </summary>
    public string Event { get; }

    /// <summary>
    /// Gets the rest: the host, the room, the reason, the detail.
    /// </summary>
    public string Details { get; }

    /// <summary>
    /// Gets a value indicating whether the event is a refusal or a failure, shown in a warning colour.
    /// </summary>
    public bool IsProblem { get; }

    /// <summary>
    /// Tells whether the row contains a text, in any column.
    /// </summary>
    /// <param name="text">The text, already trimmed.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    public bool Matches(string text)
        => text.Length == 0
           || Time.Contains(text, StringComparison.OrdinalIgnoreCase)
           || Address.Contains(text, StringComparison.OrdinalIgnoreCase)
           || Event.Contains(text, StringComparison.OrdinalIgnoreCase)
           || Details.Contains(text, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Writes the row as one line of an exported journal, with the time zone so that it can be read anywhere.
    /// </summary>
    /// <returns>The line.</returns>
    public string ToLine()
        => string.Join(
            " | ",
            Entry.Time.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture),
            Address,
            Event,
            Details);

    /// <summary>
    /// Puts the details of an event into words.
    /// </summary>
    /// <param name="entry">The event.</param>
    /// <returns>The details separated by middle dots, or an empty text.</returns>
    private static string DescribeDetails(JournalEntry entry)
    {
        var parts = new List<string>(4);
        if (entry.Host is { Length: > 0 } host)
        {
            parts.Add(Texts.Format("Monitor.Detail.Host", host));
        }

        if (entry.Room is { Length: > 0 } room)
        {
            parts.Add(Texts.Format("Monitor.Detail.Room", room));
        }

        if (entry.Reason is { } reason)
        {
            parts.Add(Texts.Get("Monitor.Reason." + reason));
        }

        if (entry.Detail is { Length: > 0 } detail)
        {
            parts.Add(entry.Event == JournalEvent.MeetingIntroduced ? Texts.Format("Monitor.Detail.HostAddress", detail) : detail);
        }

        return string.Join(" · ", parts);
    }
}
