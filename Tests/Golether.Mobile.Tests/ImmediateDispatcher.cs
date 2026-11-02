using Golether.Client.Services;

namespace Golether.Mobile.Tests;

/// <summary>
/// Runs posted work at once, on the thread of the test.
/// </summary>
internal sealed class ImmediateDispatcher : IUiDispatcher
{
    /// <inheritdoc />
    public void Post(Action action) => action();
}
