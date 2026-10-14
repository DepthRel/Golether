using Golether.Transports.Rendezvous.Protocol;

namespace Golether.Web.Server.Contracts;

/// <summary>
/// One event of the journal of the server.
/// </summary>
/// <param name="Sequence">The number of the event, growing from 1 since the server started.</param>
/// <param name="Time">When it happened.</param>
/// <param name="Event">What happened.</param>
/// <param name="Address">The address of the device the event is about (<c>ip:port</c>), or <see langword="null"/>.</param>
/// <param name="Host">The short identifier of the host device the event is about, or <see langword="null"/>.</param>
/// <param name="Room">
/// The tag of the room the event is about (the beginning of a hash of its name: the name itself lets one ask for the
/// host), or <see langword="null"/>.
/// </param>
/// <param name="Reason">Why a request was refused, or <see langword="null"/>.</param>
/// <param name="Detail">Anything else: an address, a version, an error of the system; not translated.</param>
public sealed record JournalEntry(
    long Sequence,
    DateTimeOffset Time,
    JournalEvent Event,
    string? Address = null,
    string? Host = null,
    string? Room = null,
    RendezvousError? Reason = null,
    string? Detail = null);
