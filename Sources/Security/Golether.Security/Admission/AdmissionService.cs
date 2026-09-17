using Golether.Core.Identity;
using Golether.Security.Verification;

namespace Golether.Security.Admission;

/// <summary>
/// A request of a peer to join the session.
/// </summary>
/// <param name="PeerId">The authenticated device identifier.</param>
/// <param name="DisplayName">The name the peer introduced itself with.</param>
/// <param name="VerificationCode">The code both sides compare by voice.</param>
/// <param name="IsKnownContact">Whether the device is a trusted contact.</param>
public sealed record AdmissionRequest(PeerId PeerId, string DisplayName, VerificationCode VerificationCode, bool IsKnownContact);

/// <summary>
/// The outcome of an admission request.
/// </summary>
public enum AdmissionDecision
{
    /// <summary>
    /// The peer may join.
    /// </summary>
    Approved = 0,

    /// <summary>
    /// The host rejected the peer.
    /// </summary>
    Rejected = 1,

    /// <summary>
    /// The host did not answer in time.
    /// </summary>
    TimedOut = 2,
}

/// <summary>
/// Asks the host user whether a peer may join (implemented by the UI).
/// </summary>
public interface IAdmissionPrompt
{
    /// <summary>
    /// Shows the request and waits for the answer.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled on timeout or when the peer disconnects.</param>
    /// <returns><see langword="true"/> to admit the peer.</returns>
    Task<bool> AskAsync(AdmissionRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Admission policy of the host.
/// </summary>
public sealed record AdmissionOptions
{
    /// <summary>
    /// Gets a value indicating whether trusted contacts join without a prompt (default <see langword="false"/>).
    /// </summary>
    public bool AutoApproveKnownContacts { get; init; }

    /// <summary>
    /// Gets the time the host has to answer (default 2 minutes).
    /// </summary>
    public TimeSpan PromptTimeout { get; init; } = TimeSpan.FromMinutes(2);
}

/// <summary>
/// Decides whether a peer that presented a valid invitation may join.
/// </summary>
public sealed class AdmissionService
{
    /// <summary>
    /// The prompt.
    /// </summary>
    private readonly IAdmissionPrompt _prompt;

    /// <summary>
    /// The policy.
    /// </summary>
    private readonly AdmissionOptions _options;

    /// <summary>
    /// The time provider for the prompt timeout.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Serializes prompts so the host sees one request at a time.
    /// </summary>
    private readonly SemaphoreSlim _promptGate = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="AdmissionService"/> class.
    /// </summary>
    /// <param name="prompt">The prompt.</param>
    /// <param name="options">The policy.</param>
    /// <param name="timeProvider">The time provider.</param>
    public AdmissionService(IAdmissionPrompt prompt, AdmissionOptions options, TimeProvider timeProvider)
    {
        _prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <summary>
    /// Decides on a request.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled when the peer disconnects.</param>
    /// <returns>The decision.</returns>
    public async Task<AdmissionDecision> DecideAsync(AdmissionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.IsKnownContact && _options.AutoApproveKnownContacts)
        {
            return AdmissionDecision.Approved;
        }

        using var timeout = new CancellationTokenSource(_options.PromptTimeout, _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            await _promptGate.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                return await _prompt.AskAsync(request, linked.Token).ConfigureAwait(false)
                    ? AdmissionDecision.Approved
                    : AdmissionDecision.Rejected;
            }
            finally
            {
                _promptGate.Release();
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return AdmissionDecision.TimedOut;
        }
    }
}
