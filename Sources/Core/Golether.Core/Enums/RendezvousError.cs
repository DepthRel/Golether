using System.Text.Json.Serialization;

namespace Golether.Core.Enums;

/// <summary>
/// Why a meeting through the rendezvous server did not happen. The server sends the reason, not a text: the device
/// words it in its own language (<c>Rendezvous.Error.&lt;value&gt;</c>).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<RendezvousError>))]
public enum RendezvousError
{
    /// <summary>
    /// The server and the device speak different protocol versions.
    /// </summary>
    UnsupportedVersion,

    /// <summary>
    /// The request was malformed or came at the wrong moment.
    /// </summary>
    BadRequest,

    /// <summary>
    /// The host did not prove it owns the device key it presented.
    /// </summary>
    BadSignature,

    /// <summary>
    /// Another device holds the room.
    /// </summary>
    RoomTaken,

    /// <summary>
    /// No host holds the room: the session ended or the host lost the server.
    /// </summary>
    RoomNotFound,

    /// <summary>
    /// Too many requests from this address or to this room; try again later.
    /// </summary>
    Busy,

    /// <summary>
    /// The host did not answer the meeting in time.
    /// </summary>
    Timeout,

    /// <summary>
    /// The server holds as many rooms or connections as it is allowed to.
    /// </summary>
    ServerFull,

    /// <summary>
    /// The device could not reach the server (reported by the device, never sent by the server).
    /// </summary>
    Unreachable,

    /// <summary>
    /// Both sides knew each other's addresses, but the routers let no connection through (reported by the device).
    /// </summary>
    PunchFailed,
}
