using Golether.Web.Server.Options;

namespace Golether.Web.Server.Rendezvous;

/// <summary>
/// The rooms held by hosts. A room belongs to the device key that took it first: the same key may take it again
/// (the host came back after losing the connection), another key may not.
/// </summary>
internal sealed class RoomRegistry
{
    /// <summary>
    /// The largest number of rooms.
    /// </summary>
    private readonly int _maxRooms;

    /// <summary>
    /// The rooms by name.
    /// </summary>
    private readonly Dictionary<string, HostLink> _rooms = new(StringComparer.Ordinal);

    /// <summary>
    /// Guards <see cref="_rooms"/>.
    /// </summary>
    private readonly Lock _gate = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="RoomRegistry"/> class.
    /// </summary>
    /// <param name="options">The server options.</param>
    public RoomRegistry(RendezvousServerOptions options)
    {
        _maxRooms = (options ?? throw new ArgumentNullException(nameof(options))).MaxRooms;
    }

    /// <summary>
    /// Gets the number of held rooms.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _rooms.Count;
            }
        }
    }

    /// <summary>
    /// Takes a room for a host.
    /// </summary>
    /// <param name="link">The registration of the host.</param>
    /// <param name="replaced">The earlier registration of the same key, which the caller closes.</param>
    /// <returns>The outcome.</returns>
    public RoomClaim Claim(HostLink link, out HostLink? replaced)
    {
        ArgumentNullException.ThrowIfNull(link);
        replaced = null;
        lock (_gate)
        {
            if (_rooms.TryGetValue(link.Room, out var current))
            {
                if (current.Owner != link.Owner)
                {
                    return RoomClaim.Taken;
                }

                replaced = current;
            }
            else if (_rooms.Count >= _maxRooms)
            {
                return RoomClaim.Full;
            }

            _rooms[link.Room] = link;
            return RoomClaim.Registered;
        }
    }

    /// <summary>
    /// Releases a room, unless another registration of the same key has taken it over since.
    /// </summary>
    /// <param name="link">The registration that ended.</param>
    public void Release(HostLink link)
    {
        lock (_gate)
        {
            if (_rooms.TryGetValue(link.Room, out var current) && ReferenceEquals(current, link))
            {
                _rooms.Remove(link.Room);
            }
        }
    }

    /// <summary>
    /// Finds the host of a room.
    /// </summary>
    /// <param name="room">The room.</param>
    /// <returns>The registration, or <see langword="null"/>.</returns>
    public HostLink? Find(string room)
    {
        lock (_gate)
        {
            return _rooms.GetValueOrDefault(room);
        }
    }
}
