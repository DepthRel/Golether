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

    /// <summary>
    /// The address of the source code.
    /// </summary>
    public const string SOURCE_URL = "https://github.com/DepthRel/Golether";

    /// <summary>
    /// The address of the license text of Golether.
    /// </summary>
    public const string LICENSE_URL = SOURCE_URL + "/blob/main/LICENSE.txt";

    /// <summary>
    /// The address of the list of components of other authors and their licenses.
    /// </summary>
    public const string NOTICES_URL = SOURCE_URL + "/blob/main/THIRD-PARTY-NOTICES.txt";

    /// <summary>
    /// The mail address for problems, questions and ideas.
    /// </summary>
    public const string FEEDBACK_EMAIL = "project.golether@gmail.com";

    /// <summary>
    /// Gives the address of a document of the documentation in a language. The folders of the documentation are
    /// named after the code of the language with a capital first letter (<c>Docs/Ru</c>), as the project page
    /// links them.
    /// </summary>
    /// <param name="languageCode">The two-letter code of the language.</param>
    /// <param name="file">The file of the document, such as <c>Privacy.md</c>.</param>
    /// <returns>The address of the document.</returns>
    public static string DocumentUrl(string languageCode, string file)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(languageCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(file);

        var folder = char.ToUpperInvariant(languageCode[0]) + languageCode[1..].ToLowerInvariant();

        return $"{SOURCE_URL}/blob/main/Docs/{folder}/{file}";
    }
}
