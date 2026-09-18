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
        Assert.Null(UpdateService.Choose(good with { Size = UpdateService.MaxPackageBytes + 1 }, current));
        Assert.Null(UpdateService.Choose(null, current));
        Assert.NotNull(UpdateService.Choose(good with { Url = "http://127.0.0.1:8080/x.exe" }, current));
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

        // An empty or broken address switches the check off; a broken answer is not an error either.
        service.ManifestUrl = string.Empty;
        Assert.Null(await service.CheckAsync(token));
        service.ManifestUrl = "https://example.org/updates.json";
        var broken = new UpdateService(new HttpClient(new StubHandler("<html>")), new Version(0, 1, 0)) { ManifestUrl = service.ManifestUrl };
        Assert.Null(await broken.CheckAsync(token));
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

            var path = await service.DownloadAsync(update, folder.FullName, new Progress<double>(steps.Add), token);

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
}
