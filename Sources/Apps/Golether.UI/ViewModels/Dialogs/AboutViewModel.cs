using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Golether.Core.Configuration;
using Golether.Localization;
using Golether.UI.Services;

namespace Golether.UI.ViewModels.Dialogs;

/// <summary>
/// The window about the application: the version, the project page, a word on privacy and on the license, and the
/// way to report a problem.
/// </summary>
public sealed partial class AboutViewModel : ObservableObject
{
    /// <summary>
    /// The dialogs.
    /// </summary>
    private readonly IDialogService _dialogs;

    /// <summary>
    /// The two-letter code of the language the documents are opened in.
    /// </summary>
    private readonly string _languageCode;

    /// <summary>
    /// Saves the diagnostic report, or <see langword="null"/> when reports are not offered.
    /// </summary>
    private readonly Func<Task>? _saveReport;

    /// <summary>
    /// Initializes a new instance of the <see cref="AboutViewModel"/> class.
    /// </summary>
    /// <param name="dialogs">The dialogs.</param>
    /// <param name="languageCode">The two-letter code of the language the documents are opened in.</param>
    /// <param name="saveReport">
    /// Saves the diagnostic report, or <see langword="null"/> when reports are not offered.
    /// </param>
    public AboutViewModel(IDialogService dialogs, string languageCode, Func<Task>? saveReport = null)
    {
        _saveReport = saveReport;
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        ArgumentException.ThrowIfNullOrWhiteSpace(languageCode);
        _languageCode = languageCode;
    }

    /// <summary>
    /// Gets the title of the window.
    /// </summary>
    public string Title => Texts.Get("About.Title");

    /// <summary>
    /// Gets the slogan of the project page before its highlighted word.
    /// </summary>
    public string SloganStart => Slogan().Start;

    /// <summary>
    /// Gets the highlighted word of the slogan.
    /// </summary>
    public string SloganAccent => Slogan().Accent;

    /// <summary>
    /// Gets the rest of the first line of the slogan.
    /// </summary>
    public string SloganEnd => Slogan().End;

    /// <summary>
    /// Gets the second line of the slogan.
    /// </summary>
    public string SloganSecondLine => Slogan().SecondLine;

    /// <summary>
    /// Gets the line with the version.
    /// </summary>
    public string Version => Texts.Format("About.Version", AppInfo.Version);

    /// <summary>
    /// Gets the mail address for problems, questions and ideas.
    /// </summary>
    public string FeedbackEmail => ProjectSite.FEEDBACK_EMAIL;

    /// <summary>
    /// Gets a value indicating whether the diagnostic report can be saved from here.
    /// </summary>
    public bool CanSaveReport => _saveReport is not null;

    /// <summary>
    /// Opens the project page.
    /// </summary>
    /// <returns>A task that completes when the page is handed to the browser.</returns>
    [RelayCommand]
    private Task OpenWebsiteAsync()
    {
        return _dialogs.OpenLinkAsync(ProjectSite.BASE_URL);
    }

    /// <summary>
    /// Opens the source code.
    /// </summary>
    /// <returns>A task that completes when the page is handed to the browser.</returns>
    [RelayCommand]
    private Task OpenSourceAsync()
    {
        return _dialogs.OpenLinkAsync(ProjectSite.SOURCE_URL);
    }

    /// <summary>
    /// Opens the privacy document in the language of the application.
    /// </summary>
    /// <returns>A task that completes when the page is handed to the browser.</returns>
    [RelayCommand]
    private Task OpenPrivacyAsync()
    {
        return _dialogs.OpenLinkAsync(ProjectSite.DocumentUrl(_languageCode, "Privacy.md"));
    }

    /// <summary>
    /// Opens the license text.
    /// </summary>
    /// <returns>A task that completes when the page is handed to the browser.</returns>
    [RelayCommand]
    private Task OpenLicenseAsync()
    {
        return _dialogs.OpenLinkAsync(ProjectSite.LICENSE_URL);
    }

    /// <summary>
    /// Opens the list of components of other authors and their licenses.
    /// </summary>
    /// <returns>A task that completes when the page is handed to the browser.</returns>
    [RelayCommand]
    private Task OpenNoticesAsync()
    {
        return _dialogs.OpenLinkAsync(ProjectSite.NOTICES_URL);
    }

    /// <summary>
    /// Opens a letter about a problem.
    /// </summary>
    /// <returns>A task that completes when the letter is open.</returns>
    [RelayCommand]
    private Task ReportProblemAsync()
    {
        return _dialogs.ReportProblemAsync(null);
    }

    /// <summary>
    /// Saves the diagnostic report, to attach it to the letter.
    /// </summary>
    /// <returns>A task that completes when the report is saved.</returns>
    [RelayCommand]
    private Task SaveReportAsync()
    {
        return _saveReport?.Invoke() ?? Task.CompletedTask;
    }

    /// <summary>
    /// Splits the slogan of the language pack, written as on the project page: the highlighted word between
    /// asterisks, the second line after a line break (<c>Watch movies *together*\nwherever you are</c>).
    /// </summary>
    /// <returns>The parts of the slogan; a slogan without marks is all in the first part.</returns>
    private static (string Start, string Accent, string End, string SecondLine) Slogan()
    {
        var lines = Texts.Get("About.Slogan").Split('\n', 2);
        var parts = lines[0].Split('*', 3);
        var secondLine = lines.Length > 1 ? lines[1] : string.Empty;

        return parts.Length == 3
            ? (parts[0], parts[1], parts[2], secondLine)
            : (lines[0], string.Empty, string.Empty, secondLine);
    }

    /// <summary>
    /// Copies the mail address.
    /// </summary>
    /// <returns>A task that completes when the address is copied.</returns>
    [RelayCommand]
    private Task CopyEmailAsync()
    {
        return _dialogs.CopyTextAsync(ProjectSite.FEEDBACK_EMAIL);
    }
}
