using Golether.Core.Identity;
using Golether.Security.Admission;
using Golether.Security.Verification;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Golether.Security.Tests;

/// <summary>
/// Tests of <see cref="VerificationCode"/> and <see cref="AdmissionService"/>.
/// </summary>
public sealed class VerificationAndAdmissionTests
{
    /// <summary>
    /// The first device.
    /// </summary>
    private static readonly PeerId First = PeerId.Parse(new string('1', 64));

    /// <summary>
    /// The second device.
    /// </summary>
    private static readonly PeerId Second = PeerId.Parse(new string('2', 64));

    /// <summary>
    /// The word list has 64 distinct words.
    /// </summary>
    [Fact]
    public void WordList_Has64DistinctWords()
    {
        Assert.Equal(64, VerificationCode.WordList.Count);
        Assert.Equal(64, VerificationCode.WordList.Distinct().Count());
    }

    /// <summary>
    /// Both sides compute the same code regardless of the order of the identifiers.
    /// </summary>
    [Fact]
    public void Code_IsSymmetric()
    {
        var host = VerificationCode.Compute(First, Second, "token");
        var participant = VerificationCode.Compute(Second, First, "token");

        Assert.Equal(host.ToString(), participant.ToString());
        Assert.Equal(3, host.Words.Count);
        Assert.InRange(host.Number, 0, 99);
    }

    /// <summary>
    /// Another context or another device gives another code.
    /// </summary>
    [Fact]
    public void Code_DependsOnContextAndDevices()
    {
        var codes = new[]
        {
            VerificationCode.Compute(First, Second, "a").ToString(),
            VerificationCode.Compute(First, Second, "b").ToString(),
            VerificationCode.Compute(First, PeerId.Parse(new string('3', 64)), "a").ToString(),
        };

        Assert.Equal(3, codes.Distinct().Count());
    }

    /// <summary>
    /// The host answer decides.
    /// </summary>
    /// <param name="answer">The host answer.</param>
    /// <param name="expected">The expected decision.</param>
    [Theory]
    [InlineData(true, AdmissionDecision.Approved)]
    [InlineData(false, AdmissionDecision.Rejected)]
    public async Task Admission_FollowsPrompt(bool answer, AdmissionDecision expected)
    {
        var prompt = Substitute.For<IAdmissionPrompt>();
        prompt.AskAsync(Arg.Any<AdmissionRequest>(), Arg.Any<CancellationToken>()).Returns(answer);
        var service = new AdmissionService(prompt, new AdmissionOptions(), TimeProvider.System);

        var decision = await service.DecideAsync(Request(known: false), TestContext.Current.CancellationToken);

        Assert.Equal(expected, decision);
    }

    /// <summary>
    /// Known contacts skip the prompt only when the policy allows it.
    /// </summary>
    [Fact]
    public async Task Admission_AutoApprovesKnownContactsWhenEnabled()
    {
        var prompt = Substitute.For<IAdmissionPrompt>();
        var service = new AdmissionService(prompt, new AdmissionOptions { AutoApproveKnownContacts = true }, TimeProvider.System);

        var decision = await service.DecideAsync(Request(known: true), TestContext.Current.CancellationToken);

        Assert.Equal(AdmissionDecision.Approved, decision);
        await prompt.DidNotReceiveWithAnyArgs().AskAsync(default!, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A host that does not answer in time rejects by timeout.
    /// </summary>
    [Fact]
    public async Task Admission_TimesOut()
    {
        var time = new FakeTimeProvider();
        var prompt = Substitute.For<IAdmissionPrompt>();
        prompt.AskAsync(Arg.Any<AdmissionRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => NeverAnswerAsync(call.Arg<CancellationToken>()));
        var service = new AdmissionService(prompt, new AdmissionOptions { PromptTimeout = TimeSpan.FromSeconds(5) }, time);

        var pending = service.DecideAsync(Request(known: false), TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromSeconds(6));

        Assert.Equal(AdmissionDecision.TimedOut, await pending.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A prompt that waits until it is cancelled.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that never completes successfully.</returns>
    private static async Task<bool> NeverAnswerAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return true;
    }

    /// <summary>
    /// Creates a request.
    /// </summary>
    /// <param name="known">Whether the device is a trusted contact.</param>
    /// <returns>The request.</returns>
    private static AdmissionRequest Request(bool known)
        => new(Second, "Марина", VerificationCode.Compute(First, Second, "t"), known);
}
