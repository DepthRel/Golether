namespace Golether.UI.Services;

/// <summary>
/// Runs work on the UI thread.
/// </summary>
public interface IUiDispatcher
{
    /// <summary>
    /// Queues an action on the UI thread.
    /// </summary>
    /// <param name="action">The action.</param>
    void Post(Action action);
}
