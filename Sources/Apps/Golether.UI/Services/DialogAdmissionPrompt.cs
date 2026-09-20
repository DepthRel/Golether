using Golether.Security.Admission;

namespace Golether.UI.Services;

/// <summary>
/// <see cref="IAdmissionPrompt"/> that shows the admission dialog on the UI thread.
/// </summary>
public sealed class DialogAdmissionPrompt : IAdmissionPrompt
{
    /// <summary>
    /// The dialog service.
    /// </summary>
    private readonly Func<IDialogService> _dialogs;

    /// <summary>
    /// The UI dispatcher.
    /// </summary>
    private readonly IUiDispatcher _dispatcher;

    /// <summary>
    /// Initializes a new instance of the <see cref="DialogAdmissionPrompt"/> class.
    /// </summary>
    /// <param name="dialogs">Returns the dialog service (created with the main window).</param>
    /// <param name="dispatcher">The UI dispatcher.</param>
    public DialogAdmissionPrompt(Func<IDialogService> dialogs, IUiDispatcher dispatcher)
    {
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    /// <inheritdoc />
    public Task<bool> AskAsync(AdmissionRequest request, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _dispatcher.Post(async () =>
        {
            try
            {
                completion.TrySetResult(await _dialogs().ConfirmAdmissionAsync(request, cancellationToken));
            }
            catch (OperationCanceledException)
            {
                completion.TrySetCanceled(cancellationToken);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        });
        return completion.Task.WaitAsync(cancellationToken);
    }
}
