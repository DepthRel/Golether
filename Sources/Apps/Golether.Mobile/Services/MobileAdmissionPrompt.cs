using Golether.Security.Admission;

namespace Golether.Mobile.Services;

/// <summary>
/// Asks the host on the phone whether a participant may come in: the requests wait in a line, and the screen shows
/// the first one until the host answers or the participant gives up.
/// </summary>
public sealed class MobileAdmissionPrompt : IAdmissionPrompt
{
    /// <summary>
    /// Guards the line.
    /// </summary>
    private readonly Lock _gate = new();

    /// <summary>
    /// The requests waiting for an answer, the shown one first.
    /// </summary>
    private readonly List<Pending> _waiting = [];

    /// <summary>
    /// Raised on any thread when the shown request changed.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Gets the request to show, or <see langword="null"/> when nobody waits.
    /// </summary>
    public AdmissionRequest? Current
    {
        get
        {
            lock (_gate)
            {
                return _waiting.Count > 0 ? _waiting[0].Request : null;
            }
        }
    }

    /// <inheritdoc />
    public async Task<bool> AskAsync(AdmissionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var pending = new Pending(request, new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));
        lock (_gate)
        {
            _waiting.Add(pending);
        }

        Changed?.Invoke(this, EventArgs.Empty);

        // The participant gave up or the time ran out: the request leaves the screen unanswered.
        await using var registration = cancellationToken.Register(() => Finish(pending, null));
        return await pending.Answer.Task;
    }

    /// <summary>
    /// Answers the shown request.
    /// </summary>
    /// <param name="admit">Whether the participant may come in.</param>
    /// <param name="request">The request the answer is for; a request that is gone meanwhile keeps no answer.</param>
    public void Answer(AdmissionRequest request, bool admit)
    {
        Pending? pending;
        lock (_gate)
        {
            pending = _waiting.FirstOrDefault(p => ReferenceEquals(p.Request, request));
        }

        if (pending is not null)
        {
            Finish(pending, admit);
        }
    }

    /// <summary>
    /// Declines every waiting request: the session ends.
    /// </summary>
    public void Clear()
    {
        Pending[] waiting;
        lock (_gate)
        {
            waiting = [.. _waiting];
        }

        foreach (var pending in waiting)
        {
            Finish(pending, false);
        }
    }

    /// <summary>
    /// Takes a request out of the line with its answer.
    /// </summary>
    /// <param name="pending">The request.</param>
    /// <param name="admit">The answer, or <see langword="null"/> when the request was given up.</param>
    private void Finish(Pending pending, bool? admit)
    {
        bool removed;
        lock (_gate)
        {
            removed = _waiting.Remove(pending);
        }

        if (!removed)
        {
            return;
        }

        if (admit is { } answer)
        {
            pending.Answer.TrySetResult(answer);
        }
        else
        {
            pending.Answer.TrySetCanceled();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// A request waiting for the answer of the host.
    /// </summary>
    /// <param name="Request">The request.</param>
    /// <param name="Answer">Completes with the answer.</param>
    private sealed record Pending(AdmissionRequest Request, TaskCompletionSource<bool> Answer);
}
