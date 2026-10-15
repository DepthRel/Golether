using Golether.Core.Configuration;
using Golether.Localization;
using Golether.UI.Services;

namespace Golether.UI.Tests;

/// <summary>
/// Tests of <see cref="ProblemReport"/>: the letter about a problem.
/// </summary>
public sealed class ProblemReportTests
{
    /// <summary>
    /// The letter goes to the address of the project, with the version in the subject and the facts about the
    /// application at the end; line breaks are CRLF, as mail programs expect.
    /// </summary>
    [Fact]
    public void Letter_GoesToTheProjectWithTheFacts()
    {
        var mail = ProblemReport.CreateMailto(null);
        var (subject, body) = Read(mail);
        var facts = Texts.Format("Problem.Facts", AppInfo.Version, AppInfo.System, Texts.Localizer.Current.Code);

        Assert.Equal("mailto", mail.Scheme);
        Assert.StartsWith("mailto:" + ProjectSite.FEEDBACK_EMAIL + "?", mail.OriginalString, StringComparison.Ordinal);
        Assert.Equal(Texts.Format("Problem.Subject", AppInfo.Version), subject);
        Assert.Contains(facts.Replace("\n", "\r\n"), body);
        Assert.DoesNotContain(Texts.Format("Problem.Details", string.Empty).Trim(), body);
        Assert.Contains("%0D%0A", mail.OriginalString, StringComparison.Ordinal);
    }

    /// <summary>
    /// The message the problem showed travels in the letter; a very long one is cut, so mail programs still take the
    /// address.
    /// </summary>
    [Fact]
    public void Letter_CarriesTheMessageOfTheProblem()
    {
        var (_, body) = Read(ProblemReport.CreateMailto("The video did not open\nFile is damaged"));
        var (_, longBody) = Read(ProblemReport.CreateMailto(new string('x', ProblemReport.MAX_DETAILS_LENGTH * 3)));

        Assert.Contains("The video did not open\r\nFile is damaged", body);
        Assert.Contains(new string('x', ProblemReport.MAX_DETAILS_LENGTH) + "…", longBody);
        Assert.DoesNotContain(new string('x', ProblemReport.MAX_DETAILS_LENGTH + 1), longBody);
    }

    /// <summary>
    /// Reads the subject and the body back from a <c>mailto:</c> address.
    /// </summary>
    /// <param name="mail">The address.</param>
    /// <returns>The subject and the body.</returns>
    private static (string Subject, string Body) Read(Uri mail)
    {
        var query = mail.OriginalString[(mail.OriginalString.IndexOf('?', StringComparison.Ordinal) + 1)..];
        var parts = query.Split('&')
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));

        return (parts["subject"], parts["body"]);
    }
}
