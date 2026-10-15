namespace Golether.Core.Configuration;

/// <summary>
/// The addresses of the project page (<c>Sources/Apps/Golether.Pages</c>, published with GitHub Pages).
/// </summary>
/// <remarks>
/// The page is a convenience, never a requirement: an invitation key works without it, the rendezvous server can be
/// typed by hand and the update source can be replaced in the settings.
/// </remarks>
public static class ProjectSite
{
    /// <summary>
    /// The address of the page, ending with a slash. It doubles as the page with the downloads.
    /// </summary>
    public const string BASE_URL = "https://depthrel.github.io/Golether/";

    /// <summary>
    /// The beginning of an invitation link. The invitation follows the <c>#</c>, so browsers never send it to the
    /// server of the page.
    /// </summary>
    public const string JOIN_URL_PREFIX = BASE_URL + "join/#";

    /// <summary>
    /// The address of the file that names the rendezvous server of the project.
    /// </summary>
    public const string CONFIG_URL = BASE_URL + "config.json";

    /// <summary>
    /// The address of the file that describes the latest release.
    /// </summary>
    public const string UPDATES_URL = BASE_URL + "updates.json";
}
