using Golether.Security.Admission;
using Golether.UI.ViewModels.Dialogs;

namespace Golether.UI.Services;

/// <summary>
/// Dialogs, pickers and clipboard used by view models.
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// Asks the host whether a peer may join.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancelled on timeout or when the peer leaves.</param>
    /// <returns><see langword="true"/> to admit.</returns>
    Task<bool> ConfirmAdmissionAsync(AdmissionRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Shows the invitation dialog.
    /// </summary>
    /// <param name="viewModel">The dialog view model.</param>
    /// <returns>A task that completes when the dialog is closed.</returns>
    Task ShowInviteAsync(InviteDialogViewModel viewModel);

    /// <summary>
    /// Shows the tunnel dialog.
    /// </summary>
    /// <param name="viewModel">The dialog view model.</param>
    /// <returns>A task that completes when the dialog is closed.</returns>
    Task ShowTunnelsAsync(TunnelDialogViewModel viewModel);

    /// <summary>
    /// Shows an error.
    /// </summary>
    /// <param name="title">The title.</param>
    /// <param name="message">The message.</param>
    /// <returns>A task that completes when the dialog is closed.</returns>
    Task ShowErrorAsync(string title, string message);

    /// <summary>
    /// Shows a message.
    /// </summary>
    /// <param name="title">The title.</param>
    /// <param name="message">The text.</param>
    /// <returns>A task that completes when the dialog is closed.</returns>
    Task ShowMessageAsync(string title, string message);

    /// <summary>
    /// Lets the user pick a video file.
    /// </summary>
    /// <returns>The local path, or <see langword="null"/> when cancelled.</returns>
    Task<string?> PickMediaFileAsync();

    /// <summary>
    /// Asks for a subtitle file.
    /// </summary>
    /// <returns>The path, or <see langword="null"/> when cancelled.</returns>
    Task<string?> PickSubtitleFileAsync();

    /// <summary>
    /// Asks for a sound file (an external dubbing).
    /// </summary>
    /// <returns>The path, or <see langword="null"/> when cancelled.</returns>
    Task<string?> PickAudioFileAsync();

    /// <summary>
    /// Lets the user choose where to save a tunnel configuration.
    /// </summary>
    /// <param name="suggestedName">The suggested file name.</param>
    /// <returns>The local path, or <see langword="null"/> when cancelled.</returns>
    Task<string?> PickConfigSavePathAsync(string suggestedName);

    /// <summary>
    /// Copies text to the clipboard.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>A task that completes when the text is copied.</returns>
    Task CopyTextAsync(string text);

    /// <summary>
    /// Reads text from the clipboard.
    /// </summary>
    /// <returns>The text, or <see langword="null"/>.</returns>
    Task<string?> PasteTextAsync();
}

/// <summary>
/// Runs work on the UI thread.
/// </summary>
public interface IUiDispatcher
{
    /// <summary>
    /// Queues an action on the UI thread.
    /// </summary>
    /// <param name="action">The action.</param>
    void Post(Action action);
}

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
