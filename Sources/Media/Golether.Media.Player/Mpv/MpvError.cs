namespace Golether.Media.Player.Mpv;

/// <summary>
/// Error codes of the libmpv client API used by stream callbacks.
/// </summary>
internal static class MpvError
{
    /// <summary>
    /// Loading failed (<c>MPV_ERROR_LOADING_FAILED</c>).
    /// </summary>
    public const int LoadingFailed = -13;

    /// <summary>
    /// The operation is unsupported (<c>MPV_ERROR_UNSUPPORTED</c>).
    /// </summary>
    public const int Unsupported = -18;

    /// <summary>
    /// A generic error (<c>MPV_ERROR_GENERIC</c>).
    /// </summary>
    public const int Generic = -20;
}
