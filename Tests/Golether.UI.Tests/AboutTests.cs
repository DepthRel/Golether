using NSubstitute;
using Golether.Core.Configuration;
using Golether.UI.Services;
using Golether.UI.ViewModels.Dialogs;

namespace Golether.UI.Tests;

/// <summary>
/// Tests of the window about the application and of the way to it from the settings.
/// </summary>
public sealed class AboutTests
{
    /// <summary>
    /// The dialogs substitute.
    /// </summary>
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();

    /// <summary>
    /// Every link of the window opens its page; the privacy document comes in the language of the application.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Links_OpenTheirPages()
    {
        var about = new AboutViewModel(_dialogs, "ru");

        await about.OpenWebsiteCommand.ExecuteAsync(null);
        await about.OpenSourceCommand.ExecuteAsync(null);
        await about.OpenPrivacyCommand.ExecuteAsync(null);
        await about.OpenLicenseCommand.ExecuteAsync(null);
        await about.OpenNoticesCommand.ExecuteAsync(null);

        Received.InOrder(() =>
        {
            _dialogs.OpenLinkAsync(ProjectSite.BASE_URL);
            _dialogs.OpenLinkAsync(ProjectSite.SOURCE_URL);
            _dialogs.OpenLinkAsync("https://github.com/DepthRel/Golether/blob/main/Docs/Ru/Privacy.md");
            _dialogs.OpenLinkAsync(ProjectSite.LICENSE_URL);
            _dialogs.OpenLinkAsync(ProjectSite.NOTICES_URL);
        });
    }

    /// <summary>
    /// The window shows the version and the mail address, opens a letter without a message of an error and copies
    /// the address.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Feedback_OpensALetterAndCopiesTheAddress()
    {
        var about = new AboutViewModel(_dialogs, "en");

        await about.ReportProblemCommand.ExecuteAsync(null);
        await about.CopyEmailCommand.ExecuteAsync(null);

        Assert.Contains(AppInfo.Version, about.Version, StringComparison.Ordinal);
        Assert.Equal(ProjectSite.FEEDBACK_EMAIL, about.FeedbackEmail);
        await _dialogs.Received(1).ReportProblemAsync(null);
        await _dialogs.Received(1).CopyTextAsync(ProjectSite.FEEDBACK_EMAIL);
    }

    /// <summary>
    /// The diagnostic report, which the letter asks for, is saved from the same window when reports are offered; the
    /// button is hidden otherwise.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Report_IsSavedFromTheWindow()
    {
        var saved = 0;
        var withReport = new AboutViewModel(_dialogs, "en", () =>
        {
            saved++;
            return Task.CompletedTask;
        });
        var withoutReport = new AboutViewModel(_dialogs, "en");

        await withReport.SaveReportCommand.ExecuteAsync(null);
        await withoutReport.SaveReportCommand.ExecuteAsync(null);

        Assert.True(withReport.CanSaveReport);
        Assert.False(withoutReport.CanSaveReport);
        Assert.Equal(1, saved);
    }

    /// <summary>
    /// The slogan of the project page is split into its highlighted word and its two lines, without the marks.
    /// </summary>
    [Fact]
    public void Slogan_IsSplitAsOnTheProjectPage()
    {
        var about = new AboutViewModel(_dialogs, "ru");
        var slogan = Golether.Localization.Texts.Get("About.Slogan");

        var joined = about.SloganStart + about.SloganAccent + about.SloganEnd + "\n" + about.SloganSecondLine;

        Assert.Equal(slogan.Replace("*", string.Empty), joined);
        Assert.NotEmpty(about.SloganAccent);
        Assert.NotEmpty(about.SloganSecondLine);
    }

    /// <summary>
    /// The settings open the window about the application when they are given the way to it, and hide the button
    /// otherwise.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Settings_OpenTheWindowAboutTheApplication()
    {
        var opened = 0;
        var withAbout = CreateSettings(() =>
        {
            opened++;
            return Task.CompletedTask;
        });
        var withoutAbout = CreateSettings(null);

        await withAbout.OpenAboutCommand.ExecuteAsync(null);
        await withoutAbout.OpenAboutCommand.ExecuteAsync(null);

        Assert.True(withAbout.CanOpenAbout);
        Assert.False(withoutAbout.CanOpenAbout);
        Assert.Equal(1, opened);
    }

    /// <summary>
    /// The address of a document follows the language: the folder is the code with a capital first letter.
    /// </summary>
    [Fact]
    public void DocumentUrl_FollowsTheLanguage()
    {
        const string DOCS = "https://github.com/DepthRel/Golether/blob/main/Docs/";

        Assert.Equal(DOCS + "De/Privacy.md", ProjectSite.DocumentUrl("de", "Privacy.md"));
        Assert.Equal(DOCS + "En/UserGuide.md", ProjectSite.DocumentUrl("EN", "UserGuide.md"));
    }

    /// <summary>
    /// Builds a settings view model with nothing but the way to the window about the application.
    /// </summary>
    /// <param name="openAbout">Opens the window, or <see langword="null"/> to leave it out.</param>
    /// <returns>The view model.</returns>
    private static SettingsViewModel CreateSettings(Func<Task>? openAbout)
    {
        return new SettingsViewModel(
            language: null,
            devices: null,
            conferenceAvailable: true,
            conferenceNotice: string.Empty,
            canChooseAvatar: false,
            canConfigureTunnels: true,
            avatarPreview: null,
            pickAvatar: () => Task.FromResult<AvatarSelection?>(null),
            applyAvatar: _ => Task.CompletedTask,
            openTunnels: () => Task.CompletedTask,
            openAbout: openAbout);
    }
}
