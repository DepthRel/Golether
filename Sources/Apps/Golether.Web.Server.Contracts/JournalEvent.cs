namespace Golether.Web.Server.Contracts;

/// <summary>
/// What happened on the rendezvous server, as the journal of events tells it. The monitor words every value in the
/// language of the administrator (<c>Monitor.Event.&lt;name&gt;</c>); the values are sent by name, so a new one is
/// added at the end and an old one is never renamed.
/// </summary>
public enum JournalEvent
{
    /// <summary>
    /// The server has opened everything it listens on and works (the last event of a start, after
    /// <see cref="ServerStarting"/>, <see cref="Listening"/> and <see cref="StunListening"/>).
    /// </summary>
    ServerStarted,

    /// <summary>
    /// The server is stopping.
    /// </summary>
    ServerStopping,

    /// <summary>
    /// The server listens for meetings; the detail is the TCP address.
    /// </summary>
    Listening,

    /// <summary>
    /// The server answers STUN; the detail is the UDP address.
    /// </summary>
    StunListening,

    /// <summary>
    /// A host took a room: its sessions can be met through the server.
    /// </summary>
    HostRegistered,

    /// <summary>
    /// A host left its room: its session ended or its connection broke.
    /// </summary>
    HostLeft,

    /// <summary>
    /// A host was not given a room; the reason says why.
    /// </summary>
    HostRefused,

    /// <summary>
    /// A participant asked to meet the host of a room.
    /// </summary>
    MeetingRequested,

    /// <summary>
    /// The host answered and both sides got each other's address; the detail is the address of the host.
    /// </summary>
    MeetingIntroduced,

    /// <summary>
    /// A meeting did not happen; the reason says why.
    /// </summary>
    MeetingFailed,

    /// <summary>
    /// A connection was closed at once: the address made too many of them, or the server is full.
    /// </summary>
    ConnectionRefused,

    /// <summary>
    /// A device sent something that is not the rendezvous protocol.
    /// </summary>
    BadRequest,

    /// <summary>
    /// A connection failed in an unexpected way; the detail is the error.
    /// </summary>
    ConnectionFailed,

    /// <summary>
    /// The server could not accept connections for a moment; the detail is the error of the system.
    /// </summary>
    AcceptFailed,

    /// <summary>
    /// The server begins to start (the first event of a run); the detail is its version.
    /// </summary>
    ServerStarting,

    /// <summary>
    /// A participant gave up a meeting before the host answered: it reached the host another way (directly, for
    /// example) or stopped joining. Not a failure.
    /// </summary>
    MeetingCancelled,
}
