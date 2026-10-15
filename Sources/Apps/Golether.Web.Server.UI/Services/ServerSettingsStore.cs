using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Golether.Web.Server.Contracts;
using Golether.Web.Server.Options;

namespace Golether.Web.Server.UI.Services;

/// <summary>
/// Reads and writes <c>config/appsettings.json</c> of the server. Writing keeps every part of the file the monitor
/// does not edit (the levels of the log for single sources, any other section the administrator added).
/// </summary>
/// <param name="path">The settings file.</param>
/// <param name="service">Puts the file in place with the rights of an administrator and restarts the service.</param>
public sealed class ServerSettingsStore(string path, IServiceManager service)
{
    /// <summary>
    /// The options of reading: the file may have been written by hand.
    /// </summary>
    private static readonly JsonDocumentOptions _reading = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    /// <summary>
    /// The options of writing the rendezvous section: the names as in the file, indented.
    /// </summary>
    private static readonly JsonSerializerOptions _writing = new() { WriteIndented = true };

    /// <summary>
    /// The status address read last and the time of the file it was read from.
    /// </summary>
    private (DateTime Written, Uri Url)? _status;

    /// <summary>
    /// Gets the settings file.
    /// </summary>
    public string Path { get; } = path ?? throw new ArgumentNullException(nameof(path));

    /// <summary>
    /// Reads the settings.
    /// </summary>
    /// <returns>The settings; the defaults for what the file does not say.</returns>
    /// <exception cref="IOException">The file cannot be read.</exception>
    /// <exception cref="JsonException">The file is not valid JSON.</exception>
    public ServerSettings Load()
    {
        var root = ReadRoot();
        var rendezvous = root[RendezvousServerOptions.SECTION] is JsonObject section
            ? section.Deserialize<RendezvousServerOptions>() ?? new RendezvousServerOptions()
            : new RendezvousServerOptions();
        var urls = root["Urls"]?.GetValueKind() == JsonValueKind.String ? root["Urls"]!.GetValue<string>() : ServerContract.DEFAULT_STATUS_URL;
        var level = root["Logging"]?["LogLevel"]?["Default"] is JsonValue value && value.GetValueKind() == JsonValueKind.String
            ? value.GetValue<string>()
            : ServerSettings.DEFAULT_LOG_LEVEL;
        return new ServerSettings(rendezvous, urls, level);
    }

    /// <summary>
    /// Returns the status address of the server: the first address of <c>Urls</c>, reached on this computer.
    /// </summary>
    /// <returns>The address.</returns>
    public Uri StatusUrl()
    {
        try
        {
            var written = File.GetLastWriteTimeUtc(Path);
            if (_status is { } cached && cached.Written == written)
            {
                return cached.Url;
            }

            var url = StatusUri(ReadRoot());
            _status = (written, url);
            return url;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new Uri(ServerContract.DEFAULT_STATUS_URL);
        }
    }

    /// <summary>
    /// Writes the settings. With an installed service the new file is put in place and the service restarted as one
    /// action of an administrator; without one the file is written directly.
    /// </summary>
    /// <param name="settings">The settings, already checked.</param>
    /// <param name="serviceInstalled">Whether the server runs as a service.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the settings are in place.</returns>
    /// <exception cref="ElevationCancelledException">The user declined.</exception>
    public async Task SaveAsync(ServerSettings settings, bool serviceInstalled, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var text = Render(File.Exists(Path) ? await File.ReadAllTextAsync(Path, cancellationToken).ConfigureAwait(false) : "{}", settings);
        if (!serviceInstalled)
        {
            await File.WriteAllTextAsync(Path, text, cancellationToken).ConfigureAwait(false);
            return;
        }

        var staged = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"golether-server-settings-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(staged, text, cancellationToken).ConfigureAwait(false);
        try
        {
            await service.ReplaceSettingsAndRestartAsync(staged, Path, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            File.Delete(staged);
        }
    }

    /// <summary>
    /// Puts settings into the text of a settings file, keeping everything else in it.
    /// </summary>
    /// <param name="current">The current text of the file.</param>
    /// <param name="settings">The settings.</param>
    /// <returns>The new text.</returns>
    /// <exception cref="JsonException">The current text is not a JSON object.</exception>
    public static string Render(string current, ServerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var root = JsonNode.Parse(current, documentOptions: _reading) as JsonObject ?? throw new JsonException("The settings file is not a JSON object.");
        root[RendezvousServerOptions.SECTION] = JsonSerializer.SerializeToNode(settings.Rendezvous, _writing);
        root["Urls"] = settings.StatusUrls;
        if (root["Logging"] is not JsonObject logging)
        {
            logging = new JsonObject();
            root["Logging"] = logging;
        }

        if (logging["LogLevel"] is not JsonObject levels)
        {
            levels = new JsonObject();
            logging["LogLevel"] = levels;
        }

        levels["Default"] = settings.LogLevel;
        return root.ToJsonString(_writing) + Environment.NewLine;
    }

    /// <summary>
    /// Reads the settings file as a JSON object.
    /// </summary>
    /// <returns>The object.</returns>
    private JsonObject ReadRoot()
        => JsonNode.Parse(File.ReadAllText(Path), documentOptions: _reading) as JsonObject ?? throw new JsonException("The settings file is not a JSON object.");

    /// <summary>
    /// Takes the status address from the settings: the first of <c>Urls</c>, with a "listen everywhere" host
    /// replaced by this computer.
    /// </summary>
    /// <param name="root">The settings.</param>
    /// <returns>The address.</returns>
    private static Uri StatusUri(JsonObject root)
    {
        var first = (root["Urls"]?.GetValueKind() == JsonValueKind.String ? root["Urls"]!.GetValue<string>() : null)?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (first is null)
        {
            return new Uri(ServerContract.DEFAULT_STATUS_URL);
        }

        // "+" and "*" are allowed by ASP.NET but are not hosts a URI knows.
        first = first.Replace("://+", "://0.0.0.0", StringComparison.Ordinal).Replace("://*", "://0.0.0.0", StringComparison.Ordinal);
        if (!Uri.TryCreate(first, UriKind.Absolute, out var uri))
        {
            return new Uri(ServerContract.DEFAULT_STATUS_URL);
        }

        return uri.Host is "0.0.0.0" or "[::]" or "localhost"
            ? new UriBuilder(uri) { Host = "127.0.0.1" }.Uri
            : uri;
    }

    /// <summary>
    /// Formats a whole number of seconds for a text field.
    /// </summary>
    /// <param name="time">The time.</param>
    /// <returns>The seconds.</returns>
    public static string Seconds(TimeSpan time) => ((long)time.TotalSeconds).ToString(CultureInfo.InvariantCulture);
}
