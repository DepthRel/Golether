using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace Golether.Core.Networking;

/// <summary>
/// Where a host waits for participants that cannot reach it directly: a rendezvous server and the room the host
/// holds on it.
/// </summary>
/// <remarks>
/// The server only introduces the two sides to each other and tells them the addresses their routers show to the
/// internet; the session itself goes directly between them. The room is a random name created for one hosted session,
/// so knowing the device of the host is not enough to find it.
/// </remarks>
/// <param name="Server">The rendezvous server; its TCP port introduces peers, the same UDP port answers STUN.</param>
/// <param name="Room">The room of the host (<see cref="RoomBytes"/> random bytes in base64url).</param>
public sealed record RendezvousPoint(PeerEndpoint Server, string Room)
{
    /// <summary>
    /// The number of random bytes in a room name (128 bits).
    /// </summary>
    public const int RoomBytes = 16;

    /// <summary>
    /// The length of a room name in base64url without padding.
    /// </summary>
    public const int RoomLength = 22;

    /// <summary>
    /// Gets the STUN address of the server for WebRTC, for example <c>stun://203.0.113.7:47900</c>.
    /// </summary>
    public string StunUri => "stun://" + Server;

    /// <summary>
    /// Creates a random room name.
    /// </summary>
    /// <returns>The room name.</returns>
    public static string CreateRoom() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(RoomBytes));

    /// <summary>
    /// Checks the format of a room name.
    /// </summary>
    /// <param name="room">The room name.</param>
    /// <returns><see langword="true"/> when the name decodes to <see cref="RoomBytes"/> bytes.</returns>
    public static bool IsValidRoom([NotNullWhen(true)] string? room)
    {
        // Base64Url throws on characters outside its alphabet, so the alphabet is checked first.
        if (room is not { Length: RoomLength } || !room.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            return false;
        }

        Span<byte> buffer = stackalloc byte[32];
        return Base64Url.TryDecodeFromChars(room, buffer, out var written) && written == RoomBytes;
    }

    /// <summary>
    /// Builds a point from untrusted text.
    /// </summary>
    /// <param name="server">The server as <c>host:port</c>.</param>
    /// <param name="room">The room name.</param>
    /// <param name="point">The point.</param>
    /// <returns><see langword="true"/> when both parts are valid.</returns>
    public static bool TryCreate(string? server, string? room, [NotNullWhen(true)] out RendezvousPoint? point)
    {
        point = PeerEndpoint.TryParse(server, out var endpoint) && IsValidRoom(room) ? new RendezvousPoint(endpoint, room) : null;
        return point is not null;
    }
}
