using System.Net;

namespace Golether.Web.Server.Rendezvous;

/// <summary>
/// A participant waiting for the host of a room to answer.
/// </summary>
/// <param name="Id">The identifier sent to the host.</param>
/// <param name="Host">The registration of the host.</param>
/// <param name="Participant">The public address of the participant's punching port.</param>
/// <param name="ParticipantLocals">The local addresses of that port.</param>
internal sealed record Meeting(string Id, HostLink Host, IPEndPoint Participant, IReadOnlyList<IPEndPoint> ParticipantLocals)
{
    /// <summary>
    /// Gets the answer of the host: its public address and local addresses of its punching port.
    /// </summary>
    public TaskCompletionSource<(IPEndPoint Observed, IReadOnlyList<IPEndPoint> Locals)> Answer { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
