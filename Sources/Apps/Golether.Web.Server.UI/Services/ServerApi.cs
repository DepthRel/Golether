using System.Globalization;
using System.Net.Http.Json;
using Golether.Web.Server.Contracts;

namespace Golether.Web.Server.UI.Services;

/// <summary>
/// Reads the server over its local status address.
/// </summary>
public interface IServerApi
{
    /// <summary>
    /// Reads the health report.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The report, or <see langword="null"/> when the server does not answer.</returns>
    Task<HealthReport?> GetHealthAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads the events after a number.
    /// </summary>
    /// <param name="after">The number of the last event the monitor has.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The events, or <see langword="null"/> when the server does not answer.</returns>
    Task<JournalPage?> GetJournalAsync(long after, CancellationToken cancellationToken);
}

/// <summary>
/// Reads the server over HTTP. What arrives is checked like anything from outside: a broken answer is no answer.
/// </summary>
/// <param name="http">The HTTP client; its timeout bounds every request.</param>
/// <param name="statusUrl">Returns the status address from the current settings.</param>
public sealed class ServerApi(HttpClient http, Func<Uri> statusUrl) : IServerApi
{
    /// <inheritdoc />
    public Task<HealthReport?> GetHealthAsync(CancellationToken cancellationToken)
        => GetAsync<HealthReport>(ServerContract.HealthPath, cancellationToken);

    /// <inheritdoc />
    public Task<JournalPage?> GetJournalAsync(long after, CancellationToken cancellationToken)
        => GetAsync<JournalPage>(ServerContract.JournalPath + "?after=" + after.ToString(CultureInfo.InvariantCulture), cancellationToken);

    /// <summary>
    /// Reads one answer.
    /// </summary>
    /// <typeparam name="T">The type of the answer.</typeparam>
    /// <param name="path">The path and the query.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The answer, or <see langword="null"/>.</returns>
    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return await http.GetFromJsonAsync<T>(new Uri(statusUrl(), path), ServerContract.Json, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or NotSupportedException or UriFormatException)
        {
            return null;
        }
    }
}
