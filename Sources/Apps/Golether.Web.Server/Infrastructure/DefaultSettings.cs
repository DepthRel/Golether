namespace Golether.Web.Server.Infrastructure;

/// <summary>
/// What happened to a missing settings file at the start.
/// </summary>
internal enum SettingsFileState
{
    /// <summary>
    /// The file was there.
    /// </summary>
    Existed,

    /// <summary>
    /// The file was missing and the default one was written.
    /// </summary>
    Created,

    /// <summary>
    /// The file was missing and could not be written (a read-only folder, for example): the server runs with the
    /// built-in defaults.
    /// </summary>
    Missing,
}

/// <summary>
/// The default settings of the server, built into the program (the same <c>appsettings.json</c> the packages carry).
/// They are the bottom layer of the configuration, so a missing file or a missing key never leaves the server without
/// a value: the status page stays at its usual address, the port at 47900. A missing file is written again from
/// them, so the administrator has a file to edit.
/// </summary>
internal static class DefaultSettings
{
    /// <summary>
    /// The name of the settings file.
    /// </summary>
    public const string FileName = "appsettings.json";

    /// <summary>
    /// The name of the built-in copy.
    /// </summary>
    private const string ResourceName = "Golether.Web.Server.appsettings.default.json";

    /// <summary>
    /// Opens the built-in default settings.
    /// </summary>
    /// <returns>The stream; the caller owns it.</returns>
    public static Stream Open()
        => typeof(DefaultSettings).Assembly.GetManifestResourceStream(ResourceName)
           ?? throw new InvalidOperationException($"The default settings {ResourceName} are not built into the program.");

    /// <summary>
    /// Writes the default settings file when there is none.
    /// </summary>
    /// <param name="directory">The folder of the settings.</param>
    /// <param name="problem">Why a missing file could not be written, or <see langword="null"/>.</param>
    /// <returns>What happened.</returns>
    public static SettingsFileState EnsureFile(string directory, out string? problem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        problem = null;
        var path = Path.Combine(directory, FileName);
        if (File.Exists(path))
        {
            return SettingsFileState.Existed;
        }

        try
        {
            Directory.CreateDirectory(directory);
            using var source = Open();

            // CreateNew: a file that appeared in the meantime is never overwritten.
            using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            source.CopyTo(target);
            return SettingsFileState.Created;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problem = ex.Message;
            return File.Exists(path) ? SettingsFileState.Existed : SettingsFileState.Missing;
        }
    }
}
