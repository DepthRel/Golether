using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Golether.Core.Configuration;
using Golether.Core.Data.Stores;
using Golether.Core.Networking;
using Golether.Security.Discovery;

namespace Golether.UI.Services;

/// <summary>
/// Reads the address of the rendezvous server from the page of the project.
/// </summary>
/// <remarks>
/// The file is <c>{"rendezvous":"g1.…"}</c>: the address sealed with <see cref="EndpointSeal"/>. Whatever the page
/// returns is untrusted: it is limited in size, must open as a seal and must hold a valid address. A server named by
/// a forged file could see who meets whom and refuse to help, but not read a session or pose as the host: the
/// participants still pin the key of the host from the invitation.
/// </remarks>
public sealed class RendezvousDirectory : IRendezvousDirectory
{
    /// <summary>
    /// The setting that keeps the last sealed address the page gave.
    /// </summary>
    public const string CACHE_SETTING = "network.rendezvousDirectory";

    /// <summary>
    /// The largest file accepted, in bytes.
    /// </summary>
    public const int MAX_FILE_BYTES = 4096;

    /// <summary>
    /// How long the page may take to answer before the stored address is used.
    /// </summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The HTTP client.
    /// </summary>
    private readonly HttpClient _http;

    /// <summary>
    /// The settings.
    /// </summary>
    private readonly ISettingsStore _settings;

    /// <summary>
    /// The address of the file.
    /// </summary>
    private readonly string _url;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RendezvousDirectory"/> class.
    /// </summary>
    /// <param name="http">The HTTP client.</param>
    /// <param name="settings">The settings.</param>
    /// <param name="url">The address of the file; <see cref="ProjectSite.CONFIG_URL"/> when <see langword="null"/>.</param>
    /// <param name="logger">The logger.</param>
    public RendezvousDirectory(HttpClient http, ISettingsStore settings, string? url = null, ILogger<RendezvousDirectory>? logger = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _url = url ?? ProjectSite.CONFIG_URL;
        _logger = logger ?? NullLogger<RendezvousDirectory>.Instance;
    }

    /// <inheritdoc />
    public async Task<PeerEndpoint?> FindAsync(CancellationToken cancellationToken)
    {
        var fresh = await DownloadAsync(cancellationToken).ConfigureAwait(false);
        if (EndpointSeal.TryOpen(fresh, out var endpoint))
        {
            await _settings.SetAsync(CACHE_SETTING, fresh, cancellationToken).ConfigureAwait(false);
            return endpoint;
        }

        var stored = await _settings.GetAsync(CACHE_SETTING, cancellationToken).ConfigureAwait(false);
        if (EndpointSeal.TryOpen(stored, out endpoint))
        {
            _logger.LogInformation("The project page gave no rendezvous server; the stored address is used");
            return endpoint;
        }

        return null;
    }

    /// <summary>
    /// Reads the sealed address out of a file.
    /// </summary>
    /// <param name="json">The content of the file.</param>
    /// <returns>The sealed address, or <see langword="null"/> when the file is not what is expected.</returns>
    public static string? ReadSealed(ReadOnlySpan<byte> json)
    {
        try
        {
            var reader = new Utf8JsonReader(json, new JsonReaderOptions { MaxDepth = 4 });
            using var document = JsonDocument.ParseValue(ref reader);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("rendezvous", out var value) &&
                value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Downloads the file and takes the sealed address out of it.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The sealed address, or <see langword="null"/> when the page did not answer with one.</returns>
    private async Task<string?> DownloadAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            using var response = await _http.GetAsync(_url, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MAX_FILE_BYTES)
            {
                _logger.LogInformation("The project page answered {Status} to the request for the rendezvous server", (int)response.StatusCode);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            var buffer = new byte[MAX_FILE_BYTES + 1];
            var read = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, timeout.Token).ConfigureAwait(false);
            return read > MAX_FILE_BYTES ? null : ReadSealed(buffer.AsSpan(0, read));
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException ||
            (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // The page is a convenience: without it the session still starts, with the stored address or without a
            // rendezvous server at all.
            _logger.LogInformation("The project page did not name a rendezvous server: {Error}", ex.Message);
            return null;
        }
    }
}
