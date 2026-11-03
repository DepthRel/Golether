using Golether.Core.Identity;
using Golether.Mobile.Services;
using Golether.Security.Admission;
using Golether.Security.Verification;

namespace Golether.Mobile.Tests;

/// <summary>
/// Tests of <see cref="MobileAdmissionPrompt"/>: the line of participants who wait for the answer of the host.
/// </summary>
public sealed class MobileAdmissionPromptTests
{
    /// <summary>
    /// The prompt.
    /// </summary>
    private readonly MobileAdmissionPrompt _prompt = new();

    /// <summary>
    /// Requests wait in the order they came; the answer goes to the shown one, and the next one is shown after it.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task Requests_WaitInLine()
    {
        var first = Request('a', "Ира");
        var second = Request('b', "Олег");

        var firstAnswer = _prompt.AskAsync(first, TestContext.Current.CancellationToken);
        var secondAnswer = _prompt.AskAsync(second, TestContext.Current.CancellationToken);
        Assert.Same(first, _prompt.Current);

        _prompt.Answer(first, admit: true);
        Assert.Same(second, _prompt.Current);
        _prompt.Answer(second, admit: false);

        Assert.True(await firstAnswer);
        Assert.False(await secondAnswer);
        Assert.Null(_prompt.Current);
    }

    /// <summary>
    /// A participant who gave up leaves the screen unanswered, and a late answer for them goes nowhere.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task GivenUpRequest_LeavesTheLine()
    {
        using var cancellation = new CancellationTokenSource();
        var request = Request('a', "Ира");
        var answer = _prompt.AskAsync(request, cancellation.Token);

        await cancellation.CancelAsync();
        _prompt.Answer(request, admit: true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => answer);
        Assert.Null(_prompt.Current);
    }

    /// <summary>
    /// When the session ends, everybody who waits is turned away.
    /// </summary>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task Clear_TurnsEverybodyAway()
    {
        var first = _prompt.AskAsync(Request('a', "Ира"), TestContext.Current.CancellationToken);
        var second = _prompt.AskAsync(Request('b', "Олег"), TestContext.Current.CancellationToken);

        _prompt.Clear();

        Assert.False(await first);
        Assert.False(await second);
        Assert.Null(_prompt.Current);
    }

    /// <summary>
    /// Makes a request of a device.
    /// </summary>
    /// <param name="device">The letter the fingerprint of the device is made of.</param>
    /// <param name="name">The name of the participant.</param>
    /// <returns>The request.</returns>
    private static AdmissionRequest Request(char device, string name)
        => new(PeerId.Parse(new string(device, 64)), name, new VerificationCode([1, 2, 3], 7), false);
}
