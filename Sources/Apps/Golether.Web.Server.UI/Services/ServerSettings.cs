using Golether.Web.Server.Options;

namespace Golether.Web.Server.UI.Services;

/// <summary>
/// The settings of the server the monitor edits: the <c>Rendezvous</c> section, the status address (<c>Urls</c>) and
/// how detailed the log of the service is (<c>Logging:LogLevel:Default</c>).
/// </summary>
/// <param name="Rendezvous">The rendezvous settings.</param>
/// <param name="StatusUrls">The addresses of the status page, separated by semicolons, as in <c>Urls</c>.</param>
/// <param name="LogLevel">The level of the log of the service: Trace, Debug, Information, Warning, Error, Critical or None.</param>
public sealed record ServerSettings(RendezvousServerOptions Rendezvous, string StatusUrls, string LogLevel)
{
    /// <summary>
    /// The levels of the log, from the most detailed to none at all.
    /// </summary>
    public static IReadOnlyList<string> LogLevels { get; } = ["Trace", "Debug", "Information", "Warning", "Error", "Critical", "None"];

    /// <summary>
    /// The level of the log when the file does not say.
    /// </summary>
    public const string DEFAULT_LOG_LEVEL = "Information";

    /// <summary>
    /// Checks the status addresses: every one is an <c>http</c> or <c>https</c> address with a port; the host may be
    /// <c>+</c> or <c>*</c> ("every address of this computer"), as ASP.NET allows.
    /// </summary>
    /// <param name="urls">The addresses separated by semicolons.</param>
    /// <returns><see langword="true"/> when all of them are valid.</returns>
    public static bool IsValidStatusUrls(string urls)
    {
        var parts = (urls ?? string.Empty).Split(';', StringSplitOptions.TrimEntries);
        return parts.Length > 0 && parts.All(part =>
            Uri.TryCreate(part.Replace("://+", "://0.0.0.0", StringComparison.Ordinal).Replace("://*", "://0.0.0.0", StringComparison.Ordinal), UriKind.Absolute, out var uri) &&
            uri.Scheme is "http" or "https" &&
            part.LastIndexOf(':') > part.IndexOf("://", StringComparison.Ordinal) &&
            uri.Port is > 0 and <= 65535 &&
            uri.AbsolutePath == "/");
    }
}
