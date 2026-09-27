using System.Collections.Concurrent;
using System.Net;
using Golether.Transports.Rendezvous.Protocol;
using Golether.Web.Server.Options;

namespace Golether.Web.Server.Rendezvous;

/// <summary>
/// Pairs a waiting participant with the host's answer.
/// </summary>
internal sealed class MeetingBroker
{
    /// <summary>
    /// The largest number of meetings waiting for one host.
    /// </summary>
    private readonly int _maxPendingPerRoom;

    /// <summary>
    /// The waiting meetings by identifier.
    /// </summary>
    private readonly ConcurrentDictionary<string, Meeting> _meetings = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="MeetingBroker"/> class.
    /// </summary>
    /// <param name="options">The server options.</param>
    public MeetingBroker(RendezvousServerOptions options)
    {
        _maxPendingPerRoom = (options ?? throw new ArgumentNullException(nameof(options))).MaxPendingPerRoom;
    }

    /// <summary>
    /// Opens a meeting with the host of a room.
    /// </summary>
    /// <param name="host">The registration of the host.</param>
    /// <param name="participant">The public address of the participant.</param>
    /// <param name="locals">The local addresses of the participant.</param>
    /// <returns>The meeting, or <see langword="null"/> when too many wait for this host already.</returns>
    public Meeting? Open(HostLink host, IPEndPoint participant, IReadOnlyList<IPEndPoint> locals)
    {
        if (!host.TryAddPending(_maxPendingPerRoom))
        {
            return null;
        }

        while (true)
        {
            var meeting = new Meeting(RendezvousProtocol.CreateRandomId(), host, participant, locals);
            if (_meetings.TryAdd(meeting.Id, meeting))
            {
                return meeting;
            }
        }
    }

    /// <summary>
    /// Takes the meeting a host answers; a meeting can be answered once.
    /// </summary>
    /// <param name="id">The identifier.</param>
    /// <returns>The meeting, or <see langword="null"/> when it is unknown, answered or given up.</returns>
    public Meeting? Take(string id) => _meetings.TryRemove(id, out var meeting) ? meeting : null;

    /// <summary>
    /// Ends a meeting on the participant's side, answered or not.
    /// </summary>
    /// <param name="meeting">The meeting.</param>
    public void Close(Meeting meeting)
    {
        _meetings.TryRemove(meeting.Id, out _);
        meeting.Host.RemovePending();
    }
}
