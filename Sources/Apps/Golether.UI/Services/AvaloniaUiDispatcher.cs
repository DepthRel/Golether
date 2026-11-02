using Avalonia.Threading;
using Golether.Client.Services;

namespace Golether.UI.Services;

/// <summary>
/// <see cref="IUiDispatcher"/> over the Avalonia UI thread.
/// </summary>
public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    /// <inheritdoc />
    public void Post(Action action) => Dispatcher.UIThread.Post(action);
}
