using System.Globalization;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using Golether.Core.Data.Stores;
using Golether.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Golether.UI.Services;

/// <summary>
/// Reads the manifest over HTTPS and verifies the downloaded file.
/// </summary>
/// <remarks>
/// The address is a setting: everybody may host their own builds. Nothing is installed by itself; the user decides.
/// </remarks>
public sealed class UpdateService : IUpdateService
{
    /// <summary>
    /// The setting with the address of the manifest.
    /// </summary>
    public const string UrlSetting = "updates.url";

    /// <summary>
    /// The largest manifest and package sizes accepted.
    /// </summary>
    public const long MaxPackageBytes = 512L * 1024 * 1024;

    /// <summary>
    /// The HTTP client.
    /// </summary>
    private readonly HttpClient _http;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateService"/> class.
    /// </summary>
    /// <param name="http">The HTTP client.</param>
    /// <param name="currentVersion">The version of this application.</param>
    /// <param name="logger">The logger.</param>
    public UpdateService(HttpClient http, Version? currentVersion = null, ILogger<UpdateService>? logger = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _logger = logger ?? NullLogger<UpdateService>.Instance;
        CurrentVersion = currentVersion ?? Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0);
    }

    /// <inheritdoc />
    public string ManifestUrl { get; set; } = string.Empty;

    /// <inheritdoc />
    public Version CurrentVersion { get; }

    /// <summary>
    /// Loads the address of the manifest from the settings.
    /// </summary>
    /// <param name="settings">The settings.</param>
    /// <returns>A task that completes when the address is loaded.</returns>
    public async Task LoadAsync(ISettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ManifestUrl = await settings.GetAsync(UrlSetting, CancellationToken.None).ConfigureAwait(false) ?? string.Empty;
    }

    /// <inheritdoc />
    public async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken)
    {
        if (!IsAllowed(ManifestUrl))
        {
            return null;
        }

        try
        {
            var manifest = await _http.GetFromJsonAsync<UpdateManifest>(ManifestUrl, cancellationToken).ConfigureAwait(false);
            var release = OperatingSystem.IsWindows() ? manifest?.Windows
                : OperatingSystem.IsMacOS() ? manifest?.MacOs
                : manifest?.Linux;
            return Choose(release, CurrentVersion);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or NotSupportedException or UriFormatException)
        {
            _logger.LogInformation("The update source did not answer: {Error}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Checks a release against the running version.
    /// </summary>
    /// <param name="release">The release from the manifest.</param>
    /// <param name="current">The running version.</param>
    /// <returns>The release when it is newer and sane, otherwise <see langword="null"/>.</returns>
    public static UpdateInfo? Choose(UpdateInfo? release, Version current)
    {
        if (release is null
            || !Version.TryParse(release.Version, out var version)
            || version <= current
            || !IsAllowed(release.Url)
            || release.Sha256 is not { Length: 64 } hash
            || !hash.All(char.IsAsciiHexDigit)
            || release.Size is < 0 or > MaxPackageBytes)
        {
            return null;
        }

        return release;
    }

    /// <inheritdoc />
    public async Task<string> DownloadAsync(UpdateInfo update, string folder, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (Choose(update, new Version(0, 0)) is null)
        {
            throw new InvalidDataException(Texts.Get("Update.Error.BadManifest"));
        }

        Directory.CreateDirectory(folder);
        var name = Path.GetFileName(new Uri(update.Url).LocalPath);
        var path = Path.Combine(folder, string.IsNullOrWhiteSpace(name) ? $"golether-{update.Version}.bin" : name);
        using var response = await _http.GetAsync(update.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? update.Size;
        if (total > MaxPackageBytes)
        {
            throw new InvalidDataException(Texts.Get("Update.Error.TooLarge"));
        }

        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var buffer = new byte[128 * 1024];
            long copied = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                copied += read;
                if (copied > MaxPackageBytes)
                {
                    throw new InvalidDataException(Texts.Get("Update.Error.TooLarge"));
                }

                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                progress?.Report(total > 0 ? Math.Clamp((double)copied / total, 0, 1) : 0);
            }
        }

        var actual = await ComputeHashAsync(path, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(actual, update.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(path);
            throw new InvalidDataException(Texts.Get("Update.Error.ChecksumMismatch"));
        }

        progress?.Report(1);
        return path;
    }

    /// <summary>
    /// Computes the SHA-256 of a file.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The hash in hexadecimal.</returns>
    public static async Task<string> ComputeHashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Checks that an address is an HTTPS address (or a local file for tests of a mirror).
    /// </summary>
    /// <param name="url">The address.</param>
    /// <returns><see langword="true"/> when it may be used.</returns>
    public static bool IsAllowed(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback));

    /// <summary>
    /// Formats the size of a release.
    /// </summary>
    /// <param name="bytes">The size.</param>
    /// <returns>The text.</returns>
    public static string DescribeSize(long bytes)
        => bytes <= 0 ? string.Empty : Texts.Format("Format.Megabytes", (bytes / (1024.0 * 1024)).ToString("0.#", CultureInfo.InvariantCulture));
}
