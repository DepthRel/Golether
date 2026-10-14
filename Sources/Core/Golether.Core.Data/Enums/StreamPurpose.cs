namespace Golether.Core.Data.Enums;

/// <summary>
/// The purpose of a peer stream, sent in the stream preamble.
/// </summary>
public enum StreamPurpose : byte
{
    /// <summary>
    /// Session control: hello, playback state, clock synchronization, statuses.
    /// </summary>
    Control = 1,

    /// <summary>
    /// Media data: chunk requests and responses. Several data streams run in parallel.
    /// </summary>
    MediaData = 2,

    /// <summary>
    /// The TURN traffic of cameras and voices to the relay of the host: it takes the route of the session, so the
    /// relay is reachable wherever the session is.
    /// </summary>
    Relay = 3,

    /// <summary>
    /// All TURN connections of a participant to the relay of the host in one stream: each is a channel of a
    /// multiplexer. Saves a connection through the routers per camera and voice connection.
    /// </summary>
    RelayShared = 4,
}
