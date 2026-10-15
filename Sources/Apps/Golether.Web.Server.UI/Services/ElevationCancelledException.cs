namespace Golether.Web.Server.UI.Services;

/// <summary>
/// The user declined to give the rights the action needs.
/// </summary>
public sealed class ElevationCancelledException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ElevationCancelledException"/> class.
    /// </summary>
    public ElevationCancelledException()
        : base("The user declined to run the action as administrator.")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ElevationCancelledException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    public ElevationCancelledException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ElevationCancelledException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="inner">The cause.</param>
    public ElevationCancelledException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
