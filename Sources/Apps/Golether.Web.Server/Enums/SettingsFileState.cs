namespace Golether.Web.Server.Enums;

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
