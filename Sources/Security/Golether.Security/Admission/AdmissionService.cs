using Golether.Core.Data.Enums;

namespace Golether.Security.Admission;

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
