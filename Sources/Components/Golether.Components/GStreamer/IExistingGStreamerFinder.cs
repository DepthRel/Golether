namespace Golether.Components.GStreamer;

/// <summary>
/// Finds GStreamer installations made by the official installer.
/// </summary>
public interface IExistingGStreamerFinder
{
    /// <summary>
    /// Finds installations for an architecture.
    /// </summary>
    /// <param name="runtimeIdentifier">The runtime identifier, for example <c>win-x64</c>.</param>
    /// <returns>The installations, the most suitable first.</returns>
    IReadOnlyList<ExistingGStreamer> Find(string runtimeIdentifier);

    /// <summary>
    /// Determines whether the official installer is registered for the current user or the machine. Running the
    /// installer again would take over that registration, so Golether must not do it.
    /// </summary>
    /// <returns><see langword="true"/> when a registration exists.</returns>
    bool HasInstallerRegistration();
}
