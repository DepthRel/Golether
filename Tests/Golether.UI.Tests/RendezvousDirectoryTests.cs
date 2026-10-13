using System.Net;
using System.Text;
using Golether.Core.Data.Stores;
using Golether.Core.Networking;
using Golether.Security.Discovery;
using Golether.UI.Services;

namespace Golether.UI.Tests;

/// <summary>
/// Tests of <see cref="RendezvousDirectory"/>.
/// </summary>
public sealed class RendezvousDirectoryTests
{
    /// <summary>
    /// The address the tests publish.
    /// </summary>
    private static readonly PeerEndpoint Server = PeerEndpoint.Parse("203.0.113.7:47900");

    /// <summary>
    /// The settings of the device.
    /// </summary>
    private readonly MemorySettings _settings = new();

    /// <summary>
    /// The address is read from the published file and remembered for the times the page does not answer.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Find_ReadsThePublishedAddressAndRemembersIt()
    {
        var token = TestContext.Current.CancellationToken;
        var published = EndpointSeal.Seal(Server);
        var directory = Create(new StubHandler(HttpStatusCode.OK, $$"""{ "rendezvous": "{{published}}" }"""));

        Assert.Equal(Server, await directory.FindAsync(token));
        Assert.Equal(published, _settings.Values[RendezvousDirectory.CacheSetting]);
    }

    /// <summary>
    /// When the page does not answer, answers with an error or publishes something else, the remembered address is
    /// used; the published text itself is never trusted beyond what it opens into.
    /// </summary>
    /// <param name="status">The status the page answers with.</param>
    /// <param name="body">The body the page answers with.</param>
    /// <returns>A task that completes when the test is done.</returns>
    [Theory]
    [InlineData(HttpStatusCode.NotFound, "")]
    [InlineData(HttpStatusCode.OK, "<html>not json</html>")]
    [InlineData(HttpStatusCode.OK, "{ \"rendezvous\": \"\" }")]
    [InlineData(HttpStatusCode.OK, "{ \"rendezvous\": \"203.0.113.9:1\" }")]
    [InlineData(HttpStatusCode.OK, "{ \"rendezvous\": 5 }")]
    [InlineData(HttpStatusCode.OK, "[\"g1.AAAA\"]")]
    public async Task Find_FallsBackToTheRememberedAddress(HttpStatusCode status, string body)
    {
        var token = TestContext.Current.CancellationToken;
        var remembered = EndpointSeal.Seal(Server);
        _settings.Values[RendezvousDirectory.CacheSetting] = remembered;
        var directory = Create(new StubHandler(status, body));

        Assert.Equal(Server, await directory.FindAsync(token));
        Assert.Equal(remembered, _settings.Values[RendezvousDirectory.CacheSetting]);
    }

    /// <summary>
    /// A page that cannot be reached is not an error: the remembered address is used, and without one there is none.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Find_SurvivesAnUnreachablePage()
    {
        var token = TestContext.Current.CancellationToken;
        var directory = Create(new StubHandler(new HttpRequestException("no network")));

        Assert.Null(await directory.FindAsync(token));

        _settings.Values[RendezvousDirectory.CacheSetting] = EndpointSeal.Seal(Server);
        Assert.Equal(Server, await directory.FindAsync(token));
    }

    /// <summary>
    /// A file larger than a few kilobytes is not read at all.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Find_RefusesAnOversizedFile()
    {
        var token = TestContext.Current.CancellationToken;
        var padded = $$"""{ "rendezvous": "{{EndpointSeal.Seal(Server)}}", "padding": "{{new string('x', RendezvousDirectory.MaxFileBytes)}}" }""";
        var directory = Create(new StubHandler(HttpStatusCode.OK, padded));

        Assert.Null(await directory.FindAsync(token));
        Assert.Empty(_settings.Values);
    }

    /// <summary>
    /// Creates the directory over a canned answer of the page.
    /// </summary>
    /// <param name="handler">The answer.</param>
    /// <returns>The directory.</returns>
    private RendezvousDirectory Create(HttpMessageHandler handler)
        => new(new HttpClient(handler), _settings, "https://example.org/config.json");

    /// <summary>
    /// A page with one canned answer.
    /// </summary>
    /// <param name="status">The status.</param>
    /// <param name="body">The body.</param>
    /// <param name="failure">The failure thrown instead of an answer, or <see langword="null"/>.</param>
    private sealed class StubHandler(HttpStatusCode status, string body, Exception? failure = null) : HttpMessageHandler
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="StubHandler"/> class that fails to connect.
        /// </summary>
        /// <param name="failure">The failure.</param>
        public StubHandler(Exception failure)
            : this(HttpStatusCode.OK, string.Empty, failure)
        {
        }

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => failure is not null
                ? Task.FromException<HttpResponseMessage>(failure)
                : Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body)) });
    }

    /// <summary>
    /// Settings kept in memory.
    /// </summary>
    private sealed class MemorySettings : ISettingsStore
    {
        /// <summary>
        /// Gets the stored values.
        /// </summary>
        public Dictionary<string, string> Values { get; } = [];

        /// <inheritdoc />
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken)
            => Task.FromResult(Values.GetValueOrDefault(key));

        /// <inheritdoc />
        public Task SetAsync(string key, string value, CancellationToken cancellationToken)
        {
            Values[key] = value;
            return Task.CompletedTask;
        }
    }
}
