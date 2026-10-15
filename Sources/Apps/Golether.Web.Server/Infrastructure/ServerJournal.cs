using System.Net;
using System.Security.Cryptography;
using System.Text;
using Golether.Core.Enums;
using Golether.Core.Identity;
using Golether.Transports.Rendezvous.Protocol;
using Golether.Web.Server.Contracts;

namespace Golether.Web.Server.Infrastructure;

/// <summary>
/// The latest events of the server, for the monitor on this computer. The journal lives in memory only: it holds at
/// most <see cref="CAPACITY"/> events, forgets the oldest, is never written to disk and starts empty with every run
/// of the server. It is served on the local status address only (see <see cref="ServerContract.JOURNAL_PATH"/>).
/// </summary>
/// <remarks>
/// The journal tells no more than the log of the service already does: the addresses that reached the server, the
/// short identifiers of the hosts and a tag of each room instead of its name. The class is thread-safe.
/// </remarks>
internal sealed class ServerJournal
{
    /// <summary>
    /// The largest number of events kept.
    /// </summary>
    public const int CAPACITY = 5000;

    /// <summary>
    /// The events, the oldest first.
    /// </summary>
    private readonly Queue<JournalEntry> _entries = new();

    /// <summary>
    /// Guards <see cref="_entries"/> and <see cref="_last"/>.
    /// </summary>
    private readonly Lock _gate = new();

    /// <summary>
    /// The time provider.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// The number of the newest event.
    /// </summary>
    private long _last;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerJournal"/> class.
    /// </summary>
    /// <param name="timeProvider">The time provider.</param>
    public ServerJournal(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        Started = timeProvider.GetLocalNow();
    }

    /// <summary>
    /// Gets when this run of the server started.
    /// </summary>
    public DateTimeOffset Started { get; }

    /// <summary>
    /// Adds an event.
    /// </summary>
    /// <param name="kind">What happened.</param>
    /// <param name="address">The address of the device, or <see langword="null"/>.</param>
    /// <param name="host">The host device, or <see langword="null"/>.</param>
    /// <param name="room">The room, or <see langword="null"/>; only its tag is kept.</param>
    /// <param name="reason">Why a request was refused, or <see langword="null"/>.</param>
    /// <param name="detail">Anything else, or <see langword="null"/>.</param>
    public void Record(
        JournalEvent kind,
        IPEndPoint? address = null,
        PeerId? host = null,
        string? room = null,
        RendezvousError? reason = null,
        string? detail = null)
    {
        var time = _timeProvider.GetLocalNow();
        lock (_gate)
        {
            _entries.Enqueue(new JournalEntry(
                ++_last,
                time,
                kind,
                address is null ? null : RendezvousProtocol.Format(address),
                host?.ToShortString(),
                room is null ? null : TagOf(room),
                reason,
                detail));
            while (_entries.Count > CAPACITY)
            {
                _entries.Dequeue();
            }
        }
    }

    /// <summary>
    /// Returns the events after a number.
    /// </summary>
    /// <param name="after">The number of the last event the reader has; 0 for everything kept.</param>
    /// <returns>At most <see cref="ServerContract.JOURNAL_PAGE_SIZE"/> events, the oldest first.</returns>
    public JournalPage Read(long after)
    {
        lock (_gate)
        {
            // A reader that is ahead of the journal knew an earlier run of the server: it gets everything.
            var from = after > _last ? 0 : after;
            return new JournalPage(Started, [.. _entries.Where(e => e.Sequence > from).Take(ServerContract.JOURNAL_PAGE_SIZE)], _last);
        }
    }

    /// <summary>
    /// Returns the tag of a room: the first eight hexadecimal digits of the SHA-256 of its name. Two events about the
    /// same room have the same tag, but the tag does not let anybody ask for the host of the room.
    /// </summary>
    /// <param name="room">The name of the room.</param>
    /// <returns>The tag.</returns>
    public static string TagOf(string room)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(room)).AsSpan(0, 4));
}
