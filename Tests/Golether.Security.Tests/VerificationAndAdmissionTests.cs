using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Golether.Core.Data.Enums;
using Golether.Core.Identity;
using Golether.Security.Admission;
using Golether.Security.Verification;

namespace Golether.Security.Tests;

/// <summary>
/// Tests of <see cref="VerificationCode"/> and <see cref="AdmissionService"/>.
/// </summary>
public sealed class VerificationAndAdmissionTests
{
    /// <summary>
    /// The first device.
    /// </summary>
    private static readonly PeerId _first = PeerId.Parse(new string('1', 64));

    /// <summary>
    /// The second device.
    /// </summary>
    private static readonly PeerId _second = PeerId.Parse(new string('2', 64));

    /// <summary>
    /// Every word of the list has a text of its own, and a number outside the list is refused.
    /// </summary>
    [Fact]
    public void WordList_Has64DistinctWords()
    {
        var words = Enumerable.Range(0, VerificationCode.WORD_COUNT).Select(VerificationCode.WordAt).ToArray();

        Assert.Equal(64, words.Distinct().Count());
        Assert.DoesNotContain(words, word => word.StartsWith(VerificationCode.WORD_KEY_PREFIX, StringComparison.Ordinal));
        Assert.Throws<ArgumentOutOfRangeException>(() => VerificationCode.WordAt(VerificationCode.WORD_COUNT));
        Assert.Throws<ArgumentOutOfRangeException>(() => VerificationCode.WordAt(-1));
    }

    /// <summary>
    /// The code is made of word numbers, so both sides get the same code whatever their interface languages; only the
    /// spelling of the words follows the language.
    /// </summary>
    [Fact]
    public void Code_DoesNotDependOnTheLanguage()
    {
        var code = VerificationCode.Compute(_first, _second, "token");

        Assert.Equal(3, code.WordNumbers.Count);
        Assert.All(code.WordNumbers, number => Assert.InRange(number, 0, VerificationCode.WORD_COUNT - 1));
        Assert.Equal(code.WordNumbers.Select(VerificationCode.WordAt), code.Words);
    }

    /// <summary>
    /// Both sides compute the same code regardless of the order of the identifiers.
    /// </summary>
    [Fact]
    public void Code_IsSymmetric()
    {
        var host = VerificationCode.Compute(_first, _second, "token");
        var participant = VerificationCode.Compute(_second, _first, "token");

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
            VerificationCode.Compute(_first, _second, "a").ToString(),
            VerificationCode.Compute(_first, _second, "b").ToString(),
            VerificationCode.Compute(_first, PeerId.Parse(new string('3', 64)), "a").ToString(),
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
        => new(_second, "Марина", VerificationCode.Compute(_first, _second, "t"), known);
}
