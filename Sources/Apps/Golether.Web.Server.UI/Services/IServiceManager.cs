using Golether.Web.Server.UI.Enums;

namespace Golether.Web.Server.UI.Services;

/// <summary>
/// Controls the service of the server through the tools of the system. Every action that changes something asks the
/// user for the rights of an administrator the way the system does it.
/// </summary>
public interface IServiceManager
{
    /// <summary>
    /// Asks the system for the state of the service; needs no special rights.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The state.</returns>
    Task<ServiceState> QueryAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Starts the service.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the system answered.</returns>
    /// <exception cref="ElevationCancelledException">The user declined.</exception>
    /// <exception cref="InvalidOperationException">The system refused; the message says why.</exception>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stops the service.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the system answered.</returns>
    /// <exception cref="ElevationCancelledException">The user declined.</exception>
    /// <exception cref="InvalidOperationException">The system refused; the message says why.</exception>
    Task StopAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Puts a new settings file in place and restarts the service if it runs, as one action: the user is asked once.
    /// </summary>
    /// <param name="source">The new settings, in a file the user can read.</param>
    /// <param name="target">The settings file of the server.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the system answered.</returns>
    /// <exception cref="ElevationCancelledException">The user declined.</exception>
    /// <exception cref="InvalidOperationException">The system refused; the message says why.</exception>
    Task ReplaceSettingsAndRestartAsync(string source, string target, CancellationToken cancellationToken);
}
