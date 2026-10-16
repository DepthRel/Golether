using System.Net;
using System.Security.Cryptography;
using System.Text;
using Golether.UI.Services;

namespace Golether.UI.Tests;

/// <summary>
/// Tests of <see cref="UpdateService"/>.
/// </summary>
public sealed class UpdateServiceTests
{
    /// <summary>
    /// A newer version with a sane description is offered; anything else is refused.
    /// </summary>
    [Fact]
    public void Choose_TakesOnlySaneNewerReleases()
    {
        var current = new Version(0, 1, 0);
        var good = new UpdateInfo("0.2.0", "Чат и реакции", "https://example.org/golether-0.2.0.exe", new string('a', 64), 50_000_000);

        Assert.Equal(good, UpdateService.Choose(good, current));
        Assert.Null(UpdateService.Choose(good with { Version = "0.1.0" }, current));
        Assert.Null(UpdateService.Choose(good with { Version = "не версия" }, current));
        Assert.Null(UpdateService.Choose(good with { Url = "http://example.org/x.exe" }, current));
        Assert.Null(UpdateService.Choose(good with { Url = "file:///C:/x.exe" }, current));
        Assert.Null(UpdateService.Choose(good with { Sha256 = "короткая" }, current));
        Assert.Null(UpdateService.Choose(good with { Sha256 = new string('z', 64) }, current));
        Assert.Null(UpdateService.Choose(good with { Size = UpdateService.MAX_PACKAGE_BYTES + 1 }, current));
        Assert.Null(UpdateService.Choose(null, current));
        Assert.NotNull(UpdateService.Choose(good with { Url = "http://127.0.0.1:8080/x.exe" }, current));

        // Telling about a version needs no hash; the page the user is sent to must be a safe address too.
        var plain = good with { Sha256 = null, Size = 0 };
        Assert.Equal(plain, UpdateService.Choose(plain, current));
        Assert.Equal(plain.Url, UpdateService.GetPage(plain));
        Assert.Equal("https://example.org/download", UpdateService.GetPage(plain with { Page = "https://example.org/download" }));
        Assert.Null(UpdateService.Choose(plain with { Page = "javascript:alert(1)" }, current));
        Assert.Null(UpdateService.Choose(plain with { Page = "http://example.org/download" }, current));
    }

    /// <summary>
    /// The manifest is read over HTTP and the newer release for this system is offered.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Check_ReadsTheManifest()
    {
        var token = TestContext.Current.CancellationToken;
        var manifest = """
            {
              "windows": { "version": "9.9.9", "notes": "Новое", "url": "https://example.org/golether-9.9.9.exe", "sha256": "AA11BB22CC33DD44EE55FF66007788990011223344556677889900AABBCCDDEE", "size": 1024 },
              "linux": { "version": "9.9.9", "notes": "Новое", "url": "https://example.org/golether-9.9.9.tar.gz", "sha256": "AA11BB22CC33DD44EE55FF66007788990011223344556677889900AABBCCDDEE", "size": 1024 },
              "macos": { "version": "9.9.9", "notes": "Новое", "url": "https://example.org/golether-9.9.9.zip", "sha256": "AA11BB22CC33DD44EE55FF66007788990011223344556677889900AABBCCDDEE", "size": 1024 }
            }
            """;
        var service = new UpdateService(new HttpClient(new StubHandler(manifest)), new Version(0, 1, 0))
        {
            ManifestUrl = "https://example.org/updates.json",
        };

        var found = await service.CheckAsync(token);

        Assert.NotNull(found);
        Assert.Equal(("9.9.9", "Новое"), (found.Version, found.Notes));

        // Without an address of its own the service reads the manifest of the project page.
        service.ManifestUrl = string.Empty;
        Assert.Equal(UpdateService.DEFAULT_MANIFEST_URL, service.EffectiveManifestUrl);
        Assert.NotNull(await service.CheckAsync(token));

        // The check can be switched off, and a broken address or a broken answer is not an error either.
        service.Enabled = false;
        Assert.Null(await service.CheckAsync(token));
        service.Enabled = true;
        service.ManifestUrl = "ftp://example.org/updates.json";
        Assert.Null(await service.CheckAsync(token));
        service.ManifestUrl = "https://example.org/updates.json";
        var broken = new UpdateService(new HttpClient(new StubHandler("<html>")), new Version(0, 1, 0)) { ManifestUrl = service.ManifestUrl };
        Assert.Null(await broken.CheckAsync(token));
    }

    /// <summary>
    /// A release may be announced without a hash, but it is never downloaded without one: there would be nothing to
    /// check the file against.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Download_RefusesAReleaseWithoutHash()
    {
        var folder = Directory.CreateTempSubdirectory("golether-update-");
        try
        {
            var service = new UpdateService(new HttpClient(new StubHandler("установщик")), new Version(0, 1, 0));
            var update = new UpdateInfo("0.2.0", null, "https://example.org/golether-0.2.0.exe", null, 0);

            await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(update, folder.FullName, null, TestContext.Current.CancellationToken));
            Assert.Empty(folder.GetFiles());
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    /// <summary>
    /// The downloaded file is kept only when its hash matches.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Download_ChecksTheHash()
    {
        var token = TestContext.Current.CancellationToken;
        var payload = Encoding.UTF8.GetBytes("это установщик");
        var hash = Convert.ToHexString(SHA256.HashData(payload));
        var folder = Directory.CreateTempSubdirectory("golether-update-");
        try
        {
            var service = new UpdateService(new HttpClient(new StubHandler(payload)), new Version(0, 1, 0));
            var update = new UpdateInfo("0.2.0", null, "https://example.org/golether-0.2.0.exe", hash, payload.Length);
            var steps = new List<double>();

            var path = await service.DownloadAsync(update, folder.FullName, new ListProgress(steps), token);

            Assert.Equal(Path.Combine(folder.FullName, "golether-0.2.0.exe"), path);
            Assert.Equal(payload, await File.ReadAllBytesAsync(path, token));
            Assert.Contains(1d, steps);

            var wrong = update with { Sha256 = new string('b', 64) };
            var error = await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(wrong, folder.FullName, null, token));
            Assert.Contains("Контрольная сумма", error.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(path), "The file that did not match is deleted.");
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Records every reported step at once. <see cref="Progress{T}"/> would not do: it reports on the thread pool, so
    /// the last step could still be on its way when the test looks.
    /// </summary>
    /// <param name="steps">Receives the steps.</param>
    private sealed class ListProgress(List<double> steps) : IProgress<double>
    {
        /// <inheritdoc />
        public void Report(double value) => steps.Add(value);
    }

    /// <summary>
    /// Answers a canned response to every request.
    /// </summary>
    /// <param name="payload">The body.</param>
    private sealed class StubHandler(byte[] payload) : HttpMessageHandler
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="StubHandler"/> class with text.
        /// </summary>
        /// <param name="text">The body.</param>
        public StubHandler(string text)
            : this(Encoding.UTF8.GetBytes(text))
        {
        }

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload) { Headers = { ContentType = new("application/json") } },
            });
    }

    /// <summary>
    /// Until the hash is checked the download carries a temporary name, and nothing is left behind when the check
    /// fails: no file under the name of the update, and no temporary one either.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Download_LeavesNothingBehindWhenTheCheckFails()
    {
        var token = TestContext.Current.CancellationToken;
        var payload = Encoding.UTF8.GetBytes("это установщик");
        var folder = Directory.CreateTempSubdirectory("golether-update-");
        try
        {
            var service = new UpdateService(new HttpClient(new StubHandler(payload)), new Version(0, 1, 0));
            var wrong = new UpdateInfo("0.2.0", null, "https://example.org/golether-0.2.0.exe", new string('b', 64), payload.Length);

            await Assert.ThrowsAsync<InvalidDataException>(() => service.DownloadAsync(wrong, folder.FullName, null, token));

            Assert.Empty(Directory.EnumerateFileSystemEntries(folder.FullName));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }
}
