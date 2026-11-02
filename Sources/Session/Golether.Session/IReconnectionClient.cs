using Golether.Security.Invites;

namespace Golether.Session;

/// <summary>
/// What <see cref="ParticipantReconnector"/> needs from the application that holds the session.
/// </summary>
public interface IReconnectionClient
{
    /// <summary>
    /// Gives the ticket to come back with.
    /// </summary>
    /// <param name="ended">The session being restored.</param>
    /// <returns>The ticket, or <see langword="null"/> when the user has left that session.</returns>
    Invite? GetTicket(ParticipantSession ended);

    /// <summary>
    /// Creates a session for the next attempt, connected to the application like the first one.
    /// </summary>
    /// <returns>The session.</returns>
    ParticipantSession CreateSession();

    /// <summary>
    /// Takes the restored session in place of the ended one.
    /// </summary>
    /// <param name="ended">The session that ended.</param>
    /// <param name="session">The restored session.</param>
    /// <param name="ticket">The ticket it came back with.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns><see langword="false"/> when the user left meanwhile, and the session is not needed.</returns>
    Task<bool> AdoptAsync(ParticipantSession ended, ParticipantSession session, Invite ticket, CancellationToken cancellationToken);

    /// <summary>
    /// Shows how the reconnection goes.
    /// </summary>
    /// <param name="text">The text for the user.</param>
    void Report(string text);
}
