namespace Golether.Security.Admission;

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
