using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml;
using Golether.Transports.PortMapping;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Golether.Transports.Tests;

/// <summary>
/// Tests of router port mapping with fake NAT-PMP and UPnP gateways on loopback.
/// </summary>
public sealed class PortMappingTests
{
    /// <summary>
    /// A description of a router with a WANIPConnection service.
    /// </summary>
    private const string Description = """
        <?xml version="1.0"?>
        <root xmlns="urn:schemas-upnp-org:device-1-0">
          <device>
            <deviceType>urn:schemas-upnp-org:device:InternetGatewayDevice:1</deviceType>
            <deviceList><device><deviceList><device>
              <serviceList>
                <service>
                  <serviceType>urn:schemas-upnp-org:service:WANIPConnection:1</serviceType>
                  <controlURL>/ctl/IPConn</controlURL>
                </service>
              </serviceList>
            </device></deviceList></device></deviceList>
          </device>
        </root>
        """;

    /// <summary>
    /// Public, private and shared addresses are told apart.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <param name="nonPublic">Whether it is not reachable from the Internet.</param>
    [Theory]
    [InlineData("203.0.113.7", false)]
    [InlineData("8.8.8.8", false)]
    [InlineData("10.1.2.3", true)]
    [InlineData("172.20.0.1", true)]
    [InlineData("192.168.1.1", true)]
    [InlineData("100.72.0.1", true)]
    [InlineData("169.254.3.3", true)]
    [InlineData("127.0.0.1", true)]
    public void NonPublicAddresses_AreRecognized(string address, bool nonPublic)
        => Assert.Equal(nonPublic, NetworkAddresses.IsNonPublic(IPAddress.Parse(address)));

    /// <summary>
    /// NAT-PMP maps the port, reports the public address and deletes the mapping with a zero lifetime.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task NatPmp_MapsAndUnmaps()
    {
        var token = TestContext.Current.CancellationToken;
        using var gateway = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var requests = new ConcurrentQueue<byte[]>();
        using var stop = new CancellationTokenSource();
        var server = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                var received = await gateway.ReceiveAsync(stop.Token);
                requests.Enqueue(received.Buffer);
                var response = received.Buffer[1] == 0 ? AddressResponse() : MappingResponse(received.Buffer);
                await gateway.SendAsync(response, received.RemoteEndPoint, stop.Token);
            }
        }, token);
        var client = new NatPmpClient(() => [(IPEndPoint)gateway.Client.LocalEndPoint!]);

        var mapping = await client.MapAsync(47800, TimeSpan.FromHours(1), token);

        Assert.NotNull(mapping);
        Assert.Equal("NAT-PMP", mapping.Method);
        Assert.Equal(47801, mapping.ExternalPort);
        Assert.Equal(IPAddress.Parse("203.0.113.7"), mapping.ExternalAddress);
        Assert.Equal(TimeSpan.FromSeconds(3600), mapping.Lifetime);
        Assert.True(mapping.IsPubliclyReachable);

        await client.UnmapAsync(mapping, token);
        var delete = requests.Last();
        Assert.Equal(2, delete[1]);
        Assert.Equal(47800, BinaryPrimitives.ReadUInt16BigEndian(delete.AsSpan(4)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32BigEndian(delete.AsSpan(8)));
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => server);
    }

    /// <summary>
    /// A silent gateway gives no mapping after the retransmissions.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task NatPmp_SilentGateway_GivesNothing()
    {
        using var silent = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var client = new NatPmpClient(() => [(IPEndPoint)silent.Client.LocalEndPoint!], TimeSpan.FromMilliseconds(20), attempts: 2);

        Assert.Null(await client.MapAsync(47800, TimeSpan.FromHours(1), TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Refusals and responses for another port are rejected.
    /// </summary>
    [Fact]
    public void NatPmp_ParsesOnlyMatchingSuccess()
    {
        var request = NatPmpClient.BuildMappingRequest(47800, 47800, 3600);
        var ok = MappingResponse(request);
        Assert.Equal((47801, 3600u), NatPmpClient.ParseMappingResponse(ok, 47800));
        Assert.Null(NatPmpClient.ParseMappingResponse(ok, 1234));

        var refused = (byte[])ok.Clone();
        refused[3] = 2;
        Assert.Null(NatPmpClient.ParseMappingResponse(refused, 47800));
        Assert.Null(NatPmpClient.ParseMappingResponse(ok.AsSpan(0, 10), 47800));
    }

    /// <summary>
    /// UPnP finds the router, forwards the port to this device and deletes the mapping; a router that supports only
    /// permanent leases gets a second request.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Upnp_MapsThroughFakeRouter()
    {
        var token = TestContext.Current.CancellationToken;
        using var ssdp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var descriptionUri = new Uri("http://127.0.0.1:5000/rootDesc.xml");
        var responder = Task.Run(async () =>
        {
            var search = await ssdp.ReceiveAsync(token);
            Assert.Contains("M-SEARCH", Encoding.ASCII.GetString(search.Buffer), StringComparison.Ordinal);
            var answer = $"HTTP/1.1 200 OK\r\nST: urn:schemas-upnp-org:device:InternetGatewayDevice:1\r\nLOCATION: {descriptionUri}\r\n\r\n";
            await ssdp.SendAsync(Encoding.ASCII.GetBytes(answer), search.RemoteEndPoint, token);
        }, token);

        var soap = new ConcurrentQueue<(string Action, string Body)>();
        var handler = new FakeRouter(request =>
        {
            if (request.Method == HttpMethod.Get)
            {
                Assert.Equal(descriptionUri, request.RequestUri);
                return Respond(HttpStatusCode.OK, Description);
            }

            Assert.Equal(new Uri("http://127.0.0.1:5000/ctl/IPConn"), request.RequestUri);
            var action = request.Headers.GetValues("SOAPAction").Single();
            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            soap.Enqueue((action, body));
            if (action.EndsWith("#AddPortMapping\"", StringComparison.Ordinal) && body.Contains("<NewLeaseDuration>3600<", StringComparison.Ordinal))
            {
                return Respond(HttpStatusCode.InternalServerError, "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><s:Fault><detail><UPnPError><errorCode>725</errorCode></UPnPError></detail></s:Fault></s:Body></s:Envelope>");
            }

            return action.EndsWith("#GetExternalIPAddress\"", StringComparison.Ordinal)
                ? Respond(HttpStatusCode.OK, "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><u:GetExternalIPAddressResponse xmlns:u=\"urn:schemas-upnp-org:service:WANIPConnection:1\"><NewExternalIPAddress>100.64.10.20</NewExternalIPAddress></u:GetExternalIPAddressResponse></s:Body></s:Envelope>")
                : Respond(HttpStatusCode.OK, "<ok/>");
        });
        using var http = new HttpClient(handler);
        var client = new UpnpIgdClient(http, (IPEndPoint)ssdp.Client.LocalEndPoint!, TimeSpan.FromSeconds(1));

        var mapping = await client.MapAsync(47800, TimeSpan.FromHours(1), token);
        await responder;

        Assert.NotNull(mapping);
        Assert.Equal(TimeSpan.Zero, mapping.Lifetime);
        Assert.Equal(IPAddress.Parse("100.64.10.20"), mapping.ExternalAddress);
        Assert.False(mapping.IsPubliclyReachable, "A carrier-grade NAT address is not reachable from the Internet.");
        var adds = soap.Where(s => s.Action.Contains("AddPortMapping", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, adds.Length);
        Assert.Contains("<NewInternalClient>127.0.0.1</NewInternalClient>", adds[1].Body, StringComparison.Ordinal);
        Assert.Contains("<NewExternalPort>47800</NewExternalPort>", adds[1].Body, StringComparison.Ordinal);
        Assert.Contains("<NewLeaseDuration>0</NewLeaseDuration>", adds[1].Body, StringComparison.Ordinal);

        await client.UnmapAsync(mapping, token);
        Assert.Contains(soap, s => s.Action == "\"urn:schemas-upnp-org:service:WANIPConnection:1#DeletePortMapping\"");
    }

    /// <summary>
    /// Search answers and descriptions that point to other hosts, or carry a DTD, are rejected.
    /// </summary>
    [Fact]
    public void Upnp_RejectsForeignAddressesAndDtd()
    {
        var router = IPAddress.Parse("192.168.1.1");
        Assert.Equal(new Uri("http://192.168.1.1:5000/d.xml"),
            UpnpIgdClient.ParseSearchResponse("HTTP/1.1 200 OK\r\nLocation: http://192.168.1.1:5000/d.xml\r\n\r\n", router));
        Assert.Null(UpnpIgdClient.ParseSearchResponse("HTTP/1.1 200 OK\r\nLOCATION: http://192.168.1.99:5000/d.xml\r\n\r\n", router));
        Assert.Null(UpnpIgdClient.ParseSearchResponse("HTTP/1.1 200 OK\r\nLOCATION: https://192.168.1.1/d.xml\r\n\r\n", router));
        Assert.Null(UpnpIgdClient.ParseSearchResponse("HTTP/1.1 200 OK\r\nLOCATION: http://203.0.113.7/d.xml\r\n\r\n", IPAddress.Parse("203.0.113.7")));

        var location = new Uri("http://192.168.1.1:5000/d.xml");
        Assert.Equal(new Uri("http://192.168.1.1:5000/ctl/IPConn"), UpnpIgdClient.ParseDescription(Description, location)!.ControlUri);
        var foreign = Description.Replace("/ctl/IPConn", "http://evil.example/ctl", StringComparison.Ordinal);
        Assert.Null(UpnpIgdClient.ParseDescription(foreign, location));
        var withDtd = "<?xml version=\"1.0\"?><!DOCTYPE root [<!ENTITY x \"y\">]>" + Description[(Description.IndexOf("<root", StringComparison.Ordinal))..];
        Assert.Throws<XmlException>(() => UpnpIgdClient.ParseDescription(withDtd, location));
    }

    /// <summary>
    /// SOAP arguments are escaped.
    /// </summary>
    [Fact]
    public void Upnp_EscapesSoapArguments()
    {
        var body = UpnpIgdClient.BuildSoapBody("urn:schemas-upnp-org:service:WANIPConnection:1", "AddPortMapping", [("NewPortMappingDescription", "<x>&")]);

        Assert.Contains("<NewPortMappingDescription>&lt;x&gt;&amp;</NewPortMappingDescription>", body, StringComparison.Ordinal);
        Assert.Contains("<u:AddPortMapping xmlns:u=\"urn:schemas-upnp-org:service:WANIPConnection:1\">", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The mapper falls back to the next protocol, renews the lease at half time and removes the mapping on disposal.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Mapper_FallsBackRenewsAndReleases()
    {
        var token = TestContext.Current.CancellationToken;
        var time = new FakeTimeProvider();
        var failing = Substitute.For<IPortMappingProtocol>();
        failing.MapAsync(default, default, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(Task.FromException<PortMappingResult?>(new SocketException()));
        var working = Substitute.For<IPortMappingProtocol>();
        var mapping = new PortMappingResult("UPnP", 47800, 47800, IPAddress.Parse("203.0.113.7"), TimeSpan.FromHours(1));
        working.MapAsync(47800, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(mapping);
        var mapper = new PortMapper([failing, working], time);

        var lease = await mapper.MapAsync(47800, token);

        Assert.NotNull(lease);
        Assert.Same(mapping, lease.Mapping);
        await working.Received(1).MapAsync(47800, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
        time.Advance(TimeSpan.FromMinutes(31));
        for (var i = 0; i < 100 && working.ReceivedCalls().Count() < 2; i++)
        {
            await Task.Delay(10, token);
        }

        await working.Received(2).MapAsync(47800, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
        await lease.DisposeAsync();
        await working.Received(1).UnmapAsync(mapping, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Builds a successful mapping response for a request, granting the next port.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns>The response.</returns>
    private static byte[] MappingResponse(byte[] request)
    {
        var response = new byte[16];
        response[1] = 130;
        request.AsSpan(4, 2).CopyTo(response.AsSpan(8));
        var suggested = BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(6));
        BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(10), (ushort)(suggested == 0 ? 0 : suggested + 1));
        request.AsSpan(8, 4).CopyTo(response.AsSpan(12));
        return response;
    }

    /// <summary>
    /// Builds a public address response.
    /// </summary>
    /// <returns>The response.</returns>
    private static byte[] AddressResponse() => [0, 128, 0, 0, 0, 0, 0, 1, 203, 0, 113, 7];

    /// <summary>
    /// Creates an HTTP response.
    /// </summary>
    /// <param name="status">The status.</param>
    /// <param name="body">The body.</param>
    /// <returns>The response.</returns>
    private static HttpResponseMessage Respond(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "text/xml") };

    /// <summary>
    /// An HTTP handler that answers like a router.
    /// </summary>
    /// <param name="respond">Produces the responses.</param>
    private sealed class FakeRouter(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}
