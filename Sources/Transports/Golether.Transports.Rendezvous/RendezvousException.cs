using Golether.Localization;
using Golether.Transports.Rendezvous.Protocol;

namespace Golether.Transports.Rendezvous;

/// <summary>
/// A meeting through the rendezvous server did not happen. It is an <see cref="IOException"/>, so callers that
/// already handle a failed connection handle it too.
/// </summary>
public sealed class RendezvousException : IOException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RendezvousException"/> class.
    /// </summary>
    /// <param name="error">The reason.</param>
    /// <param name="innerException">The cause, or <see langword="null"/>.</param>
    public RendezvousException(RendezvousError error, Exception? innerException = null)
        : base(Describe(error), innerException)
    {
        Error = error;
    }

    /// <summary>
    /// Gets the reason.
    /// </summary>
    public RendezvousError Error { get; }

    /// <summary>
    /// Words a reason in the language of the application.
    /// </summary>
    /// <param name="error">The reason.</param>
    /// <returns>The text.</returns>
    public static string Describe(RendezvousError error) => Texts.Get("Rendezvous.Error." + error);
}
