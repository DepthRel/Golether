using System.Text;
using System.Text.Json;
using Golether.UI.Services;

namespace Golether.UI.Tests;

/// <summary>
/// Tests of <see cref="SingleInstance"/>.
/// </summary>
public sealed class SingleInstanceTests
{
    /// <summary>
    /// The first copy takes the name; a later start hands its arguments over to it instead of starting.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task LaterStart_HandsItsArgumentsToTheRunningCopy()
    {
        var name = SingleInstance.GetName(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        using var running = SingleInstance.TryAcquire(name);
        Assert.NotNull(running);
        Assert.Null(SingleInstance.TryAcquire(name));

        var received = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        running.SetHandler(arguments => received.TrySetResult(arguments));

        Assert.True(SingleInstance.Forward(name, ["golether://join/v1/abc", "второй"]));
        Assert.Equal(["golether://join/v1/abc", "второй"], await received.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A link that arrives while the window is still being prepared waits for the handler instead of being lost.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Arguments_WaitForTheHandler()
    {
        var name = SingleInstance.GetName(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        using var running = SingleInstance.TryAcquire(name);
        Assert.NotNull(running);
        Assert.True(SingleInstance.Forward(name, ["golether://join/v1/early"]));

        // The message is on its way through the pipe; the handler appears only afterwards.
        await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
        var received = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        running.SetHandler(arguments => received.TrySetResult(arguments));

        Assert.Equal(["golether://join/v1/early"], await received.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Once the running copy is gone, the name is free again.
    /// </summary>
    [Fact]
    public void Name_IsFreeAfterTheRunningCopyEnds()
    {
        var name = SingleInstance.GetName(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        SingleInstance.TryAcquire(name)!.Dispose();

        using var next = SingleInstance.TryAcquire(name);

        Assert.NotNull(next);
    }

    /// <summary>
    /// The name belongs to one data folder: a development copy and an installed one do not answer for each other.
    /// </summary>
    [Fact]
    public void Name_DependsOnTheDataFolder()
    {
        var first = Path.Combine(Path.GetTempPath(), "golether-a");
        var second = Path.Combine(Path.GetTempPath(), "golether-b");

        Assert.Equal(SingleInstance.GetName(first), SingleInstance.GetName(first + Path.DirectorySeparatorChar));
        Assert.NotEqual(SingleInstance.GetName(first), SingleInstance.GetName(second));
        Assert.Matches("^Golether-[0-9A-F]{24}$", SingleInstance.GetName(first));
    }

    /// <summary>
    /// Only a short list of strings is taken for arguments; anything else that comes through the pipe is dropped.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="accepted">Whether it is taken.</param>
    [Theory]
    [InlineData("[\"golether://join/v1/abc\"]", true)]
    [InlineData("[]", true)]
    [InlineData("", false)]
    [InlineData("not json", false)]
    [InlineData("{\"a\":1}", false)]
    [InlineData("[1,2]", false)]
    [InlineData("[null]", false)]
    [InlineData("[\"1\",\"2\",\"3\",\"4\",\"5\",\"6\",\"7\",\"8\",\"9\"]", false)]
    public void ParseMessage_TakesOnlyAShortListOfStrings(string message, bool accepted)
    {
        Assert.Equal(accepted, SingleInstance.ParseMessage(Encoding.UTF8.GetBytes(message)) is not null);
    }

    /// <summary>
    /// A message beyond the size limit is dropped whatever it holds.
    /// </summary>
    [Fact]
    public void ParseMessage_RefusesAnOversizedMessage()
    {
        var message = JsonSerializer.SerializeToUtf8Bytes(new[] { new string('x', SingleInstance.MaxMessageBytes) });

        Assert.Null(SingleInstance.ParseMessage(message));
    }
}
