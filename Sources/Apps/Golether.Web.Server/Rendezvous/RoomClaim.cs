namespace Golether.Web.Server.Rendezvous;

/// <summary>
/// The outcome of taking a room.
/// </summary>
internal enum RoomClaim
{
    /// <summary>
    /// The room is held by the host now.
    /// </summary>
    Registered,

    /// <summary>
    /// Another device key holds the room.
    /// </summary>
    Taken,

    /// <summary>
    /// The server holds as many rooms as allowed.
    /// </summary>
    Full,
}
