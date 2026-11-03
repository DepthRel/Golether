using Golether.Security.Invites;

namespace Golether.Mobile.Services;

/// <summary>
/// The place of this device in a session it joined by an invitation.
/// </summary>
public interface IParticipantConnection : ISessionConnection
{
    /// <summary>
    /// Connects to the host of the invitation.
    /// </summary>
    /// <param name="invite">The invitation.</param>
    /// <param name="cancellationToken">Stops joining.</param>
    /// <returns>A task that completes when the host has answered.</returns>
    Task JoinAsync(Invite invite, CancellationToken cancellationToken);
}
