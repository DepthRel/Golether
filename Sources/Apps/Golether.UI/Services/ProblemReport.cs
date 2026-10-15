using Golether.Core.Configuration;
using Golether.Localization;

namespace Golether.UI.Services;

/// <summary>
/// Writes the letter about a problem: a <c>mailto:</c> address that opens the mail program of the user with the
/// subject, the questions to answer and the facts about the application already filled in. Nothing is sent until the
/// user sends the letter.
/// </summary>
public static class ProblemReport
{
    /// <summary>
    /// The longest message of the application the letter carries; mail programs refuse very long <c>mailto:</c>
    /// addresses.
    /// </summary>
    public const int MAX_DETAILS_LENGTH = 1000;

    /// <summary>
    /// Writes the letter about a problem.
    /// </summary>
    /// <param name="details">The message of the application the problem showed, or <see langword="null"/>.</param>
    /// <returns>The <c>mailto:</c> address of the letter.</returns>
    public static Uri CreateMailto(string? details)
    {
        var subject = Texts.Format("Problem.Subject", AppInfo.Version);
        var facts = Texts.Format("Problem.Facts", AppInfo.Version, AppInfo.System, Texts.Localizer.Current.Code);
        var body = Texts.Format(
            "Problem.Body",
            Texts.Get("Main.Settings"),
            Texts.Get("Settings.About"),
            Texts.Get("Main.Report"));

        if (!string.IsNullOrWhiteSpace(details))
        {
            var trimmed = details.Trim();
            if (trimmed.Length > MAX_DETAILS_LENGTH)
            {
                trimmed = trimmed[..MAX_DETAILS_LENGTH] + "…";
            }

            body += "\n\n" + Texts.Format("Problem.Details", trimmed);
        }

        body += "\n\n---\n" + facts;

        var query = "subject=" + Escape(subject) + "&body=" + Escape(body);

        return new Uri($"mailto:{ProjectSite.FEEDBACK_EMAIL}?{query}");
    }

    /// <summary>
    /// Escapes a part of a <c>mailto:</c> address; line breaks become CRLF, as RFC 6068 asks.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The escaped text.</returns>
    private static string Escape(string text)
    {
        return Uri.EscapeDataString(text.Replace("\r\n", "\n").Replace("\n", "\r\n"));
    }
}
