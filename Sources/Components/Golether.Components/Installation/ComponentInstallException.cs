namespace Golether.Components.Installation;

/// <summary>
/// An installation failed.
/// </summary>
public sealed class ComponentInstallException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ComponentInstallException"/> class.
    /// </summary>
    /// <param name="message">The message for the user.</param>
    /// <param name="innerException">The cause.</param>
    public ComponentInstallException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
