using System.Text.Json;

namespace Golether.Web.Server.UI.Services;

/// <summary>
/// The settings of the monitor itself, kept between starts: for now the language. They live in the folder of the
/// server, in <c>config/monitor.json</c> next to the settings of the server, but in a file of their own: changing them
/// never touches the server and never restarts it.
/// </summary>
/// <param name="path">The file of the settings.</param>
public sealed class MonitorPreferences(string path)
{
    /// <summary>
    /// The name of the file.
    /// </summary>
    public const string FILE_NAME = "monitor.json";

    /// <summary>
    /// Returns the settings file of the monitor next to the settings file of the server.
    /// </summary>
    /// <param name="paths">Where the server keeps its settings.</param>
    /// <returns>The settings of the monitor.</returns>
    public static MonitorPreferences NextTo(ServerPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return new MonitorPreferences(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(paths.SettingsFile)!, FILE_NAME));
    }

    /// <summary>
    /// Gets the file of the settings.
    /// </summary>
    public string Path { get; } = path ?? throw new ArgumentNullException(nameof(path));

    /// <summary>
    /// Reads the chosen language.
    /// </summary>
    /// <returns>The code of the language, or <see langword="null"/> when none was chosen or the file is unreadable.</returns>
    public string? LoadLanguage()
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path));
            return document.RootElement.TryGetProperty("language", out var language) && language.ValueKind == JsonValueKind.String &&
                language.GetString() is { Length: > 0 } code
                ? code
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Remembers the chosen language.
    /// </summary>
    /// <param name="code">The code of the language.</param>
    /// <returns>
    /// <see langword="false"/> when the file cannot be written (the folder of the server needs rights the user does
    /// not have): the language still changes, but only until the monitor is closed.
    /// </returns>
    public bool SaveLanguage(string code)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(new Dictionary<string, string> { ["language"] = code }, _indented) + Environment.NewLine);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// The format of the file: indented, so it reads like the settings of the server.
    /// </summary>
    private static readonly JsonSerializerOptions _indented = new() { WriteIndented = true };
}
