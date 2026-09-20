using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Golether.Transports.PortMapping;

/// <summary>
/// UPnP Internet Gateway Device: the router is found with SSDP and asked over SOAP to forward a port.
/// </summary>
/// <remarks>
/// Only devices of the local network are used, and the description and control addresses must point to the device
/// that answered the search, so a response cannot direct requests to other hosts.
/// </remarks>
public sealed class UpnpIgdClient : IPortMappingProtocol
{
    /// <summary>
    /// The SSDP multicast group and port.
    /// </summary>
    public static readonly IPEndPoint SsdpMulticast = new(IPAddress.Parse("239.255.255.250"), 1900);

    /// <summary>
    /// The largest description or SOAP response that is read.
    /// </summary>
    private const int MaxDocumentSize = 256 * 1024;

    /// <summary>
    /// The UPnP error "only permanent leases are supported".
    /// </summary>
    private const string OnlyPermanentLeases = "725";

    /// <summary>
    /// The search targets, from the most to the least specific.
    /// </summary>
    private static readonly string[] SearchTargets =
    [
        "urn:schemas-upnp-org:device:InternetGatewayDevice:1",
        "urn:schemas-upnp-org:device:InternetGatewayDevice:2",
    ];

    /// <summary>
    /// The connection services that can forward ports.
    /// </summary>
    private static readonly string[] ServiceTypes =
    [
        "urn:schemas-upnp-org:service:WANIPConnection:2",
        "urn:schemas-upnp-org:service:WANIPConnection:1",
        "urn:schemas-upnp-org:service:WANPPPConnection:1",
    ];

    /// <summary>
    /// The HTTP client.
    /// </summary>
    private readonly HttpClient _http;

    /// <summary>
    /// Where the search is sent.
    /// </summary>
    private readonly IPEndPoint _searchTarget;

    /// <summary>
    /// How long answers to the search are collected.
    /// </summary>
    private readonly TimeSpan _searchTime;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// The service that opened the last mapping.
    /// </summary>
    private GatewayService? _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpnpIgdClient"/> class.
    /// </summary>
    /// <param name="http">The HTTP client; requests use their own time limits.</param>
    /// <param name="searchTarget">Where the search is sent; <see cref="SsdpMulticast"/> when <see langword="null"/>.</param>
    /// <param name="searchTime">How long answers are collected (2 s when <see langword="null"/>).</param>
    /// <param name="logger">The logger.</param>
    public UpnpIgdClient(HttpClient http, IPEndPoint? searchTarget = null, TimeSpan? searchTime = null, ILogger<UpnpIgdClient>? logger = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _searchTarget = searchTarget ?? SsdpMulticast;
        _searchTime = searchTime ?? TimeSpan.FromSeconds(2);
        _logger = logger ?? NullLogger<UpnpIgdClient>.Instance;
    }

    /// <inheritdoc />
    public string Name => "UPnP";

    /// <inheritdoc />
    public async Task<PortMappingResult?> MapAsync(int port, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, ushort.MaxValue);
        foreach (var location in await SearchAsync(cancellationToken).ConfigureAwait(false))
        {
            GatewayService? service;
            try
            {
                service = await DescribeAsync(location, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or XmlException or InvalidDataException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug("UPnP description {Location} failed: {Error}", location, ex.Message);
                continue;
            }

            if (service is null)
            {
                continue;
            }

            var mapped = await TryAddMappingAsync(service, port, lifetime, cancellationToken).ConfigureAwait(false);
            if (mapped is null)
            {
                continue;
            }

            _service = service;
            var address = await TryGetExternalAddressAsync(service, cancellationToken).ConfigureAwait(false);
            return new PortMappingResult(Name, port, port, address, mapped.Value);
        }

        return null;
    }

    /// <inheritdoc />
    public async Task UnmapAsync(PortMappingResult mapping, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        if (_service is { } service)
        {
            await SendSoapAsync(service, "DeletePortMapping",
                [("NewRemoteHost", string.Empty), ("NewExternalPort", Number(mapping.ExternalPort)), ("NewProtocol", "TCP")],
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Builds an SSDP search request.
    /// </summary>
    /// <param name="searchTarget">The search target.</param>
    /// <returns>The request bytes.</returns>
    internal static byte[] BuildSearchRequest(string searchTarget)
        => Encoding.ASCII.GetBytes(
            "M-SEARCH * HTTP/1.1\r\n" +
            "HOST: 239.255.255.250:1900\r\n" +
            "MAN: \"ssdp:discover\"\r\n" +
            "MX: 2\r\n" +
            $"ST: {searchTarget}\r\n\r\n");

    /// <summary>
    /// Extracts the description address from an SSDP answer and checks that it belongs to the answering device.
    /// </summary>
    /// <param name="response">The answer.</param>
    /// <param name="sender">The address of the answering device.</param>
    /// <returns>The description address, or <see langword="null"/> when the answer is unusable.</returns>
    internal static Uri? ParseSearchResponse(string response, IPAddress sender)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (!response.StartsWith("HTTP/1.1 200", StringComparison.OrdinalIgnoreCase) || !NetworkAddresses.IsLocalNetwork(sender))
        {
            return null;
        }

        foreach (var line in response.Split("\r\n"))
        {
            var colon = line.IndexOf(':');
            if (colon > 0 && line[..colon].Trim().Equals("LOCATION", StringComparison.OrdinalIgnoreCase)
                && Uri.TryCreate(line[(colon + 1)..].Trim(), UriKind.Absolute, out var location)
                && location.Scheme == Uri.UriSchemeHttp
                && IPAddress.TryParse(location.Host, out var host) && host.Equals(sender))
            {
                return location;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the port forwarding service in a device description.
    /// </summary>
    /// <param name="description">The description XML.</param>
    /// <param name="location">The description address.</param>
    /// <returns>The service, or <see langword="null"/> when there is none or it points to another host.</returns>
    /// <exception cref="XmlException">The XML is invalid or contains a DTD.</exception>
    internal static GatewayService? ParseDescription(string description, Uri location)
    {
        using var reader = XmlReader.Create(new StringReader(description), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var document = XDocument.Load(reader);
        var services = document.Descendants().Where(e => e.Name.LocalName == "service").ToArray();
        var baseText = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "URLBase")?.Value.Trim();
        var baseUri = Uri.TryCreate(baseText, UriKind.Absolute, out var parsed) ? parsed : location;
        foreach (var type in ServiceTypes)
        {
            var service = services.FirstOrDefault(s => Child(s, "serviceType") == type);
            var control = service is null ? null : Child(service, "controlURL");
            if (string.IsNullOrEmpty(control) || !Uri.TryCreate(baseUri, control, out var controlUri))
            {
                continue;
            }

            // The control address must stay on the device that answered the search.
            if (controlUri.Scheme != Uri.UriSchemeHttp || controlUri.Host != location.Host)
            {
                return null;
            }

            return new GatewayService(type, controlUri, IPAddress.Parse(location.Host));
        }

        return null;
    }

    /// <summary>
    /// Builds a SOAP request body.
    /// </summary>
    /// <param name="serviceType">The service type.</param>
    /// <param name="action">The action.</param>
    /// <param name="arguments">The arguments in order.</param>
    /// <returns>The XML text.</returns>
    internal static string BuildSoapBody(string serviceType, string action, IReadOnlyList<(string Name, string Value)> arguments)
    {
        XNamespace soap = "http://schemas.xmlsoap.org/soap/envelope/";
        XNamespace u = serviceType;
        var envelope = new XElement(
            soap + "Envelope",
            new XAttribute(XNamespace.Xmlns + "s", soap),
            new XAttribute(soap + "encodingStyle", "http://schemas.xmlsoap.org/soap/encoding/"),
            new XElement(soap + "Body", new XElement(u + action, new XAttribute(XNamespace.Xmlns + "u", u), arguments.Select(a => new XElement(a.Name, a.Value)))));
        return "<?xml version=\"1.0\"?>" + envelope.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>
    /// Reads a child element value.
    /// </summary>
    /// <param name="element">The parent.</param>
    /// <param name="name">The local name.</param>
    /// <returns>The trimmed value, or <see langword="null"/>.</returns>
    private static string? Child(XElement element, string name)
        => element.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim();

    /// <summary>
    /// Formats a number for SOAP.
    /// </summary>
    /// <param name="value">The number.</param>
    /// <returns>The text.</returns>
    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Sends the search and collects description addresses.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The distinct addresses in answer order.</returns>
    private async Task<IReadOnlyList<Uri>> SearchAsync(CancellationToken cancellationToken)
    {
        var found = new List<Uri>();
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        try
        {
            foreach (var target in SearchTargets)
            {
                await udp.SendAsync(BuildSearchRequest(target), _searchTarget, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (SocketException ex)
        {
            _logger.LogDebug("SSDP search failed: {Error}", ex.SocketErrorCode);
            return found;
        }

        using var window = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        window.CancelAfter(_searchTime);
        try
        {
            while (found.Count < 8)
            {
                var answer = await udp.ReceiveAsync(window.Token).ConfigureAwait(false);
                var location = ParseSearchResponse(Encoding.ASCII.GetString(answer.Buffer), answer.RemoteEndPoint.Address);
                if (location is not null && !found.Contains(location))
                {
                    found.Add(location);
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The search window is over.
        }
        catch (SocketException ex)
        {
            _logger.LogDebug("SSDP receive failed: {Error}", ex.SocketErrorCode);
        }

        return found;
    }

    /// <summary>
    /// Downloads a device description.
    /// </summary>
    /// <param name="location">The description address.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The service, or <see langword="null"/>.</returns>
    private async Task<GatewayService?> DescribeAsync(Uri location, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, location);
        var text = await SendLimitedAsync(request, cancellationToken).ConfigureAwait(false);
        return text.Success ? ParseDescription(text.Body, location) : null;
    }

    /// <summary>
    /// Adds the mapping, retrying with a permanent lease when the router supports only those.
    /// </summary>
    /// <param name="service">The service.</param>
    /// <param name="port">The port.</param>
    /// <param name="lifetime">The requested lease.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The granted lease, or <see langword="null"/> when the router refused.</returns>
    private async Task<TimeSpan?> TryAddMappingAsync(GatewayService service, int port, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        var client = NetworkAddresses.GetLocalAddressFor(service.Device).ToString();
        foreach (var lease in new[] { lifetime, TimeSpan.Zero })
        {
            var response = await SendSoapAsync(service, "AddPortMapping",
            [
                ("NewRemoteHost", string.Empty),
                ("NewExternalPort", Number(port)),
                ("NewProtocol", "TCP"),
                ("NewInternalPort", Number(port)),
                ("NewInternalClient", client),
                ("NewEnabled", "1"),
                ("NewPortMappingDescription", "Golether"),
                ("NewLeaseDuration", Number((long)lease.TotalSeconds)),
            ], cancellationToken).ConfigureAwait(false);
            if (response.Success)
            {
                return lease;
            }

            if (!response.Body.Contains("<errorCode>" + OnlyPermanentLeases + "</errorCode>", StringComparison.Ordinal))
            {
                _logger.LogInformation("The router refused the UPnP mapping of port {Port}", port);
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Asks the router for its public address.
    /// </summary>
    /// <param name="service">The service.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The address, or <see langword="null"/>.</returns>
    private async Task<IPAddress?> TryGetExternalAddressAsync(GatewayService service, CancellationToken cancellationToken)
    {
        var response = await SendSoapAsync(service, "GetExternalIPAddress", [], cancellationToken).ConfigureAwait(false);
        if (!response.Success)
        {
            return null;
        }

        try
        {
            using var reader = XmlReader.Create(new StringReader(response.Body), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var value = XDocument.Load(reader).Descendants().FirstOrDefault(e => e.Name.LocalName == "NewExternalIPAddress")?.Value.Trim();
            return IPAddress.TryParse(value, out var address) && address.AddressFamily == AddressFamily.InterNetwork ? address : null;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    /// <summary>
    /// Calls a SOAP action.
    /// </summary>
    /// <param name="service">The service.</param>
    /// <param name="action">The action.</param>
    /// <param name="arguments">The arguments.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Whether the call succeeded and the response body.</returns>
    private async Task<(bool Success, string Body)> SendSoapAsync(GatewayService service, string action, IReadOnlyList<(string Name, string Value)> arguments, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, service.ControlUri)
        {
            Content = new StringContent(BuildSoapBody(service.ServiceType, action, arguments), Encoding.UTF8),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("text/xml") { CharSet = "utf-8" };
        request.Headers.TryAddWithoutValidation("SOAPAction", $"\"{service.ServiceType}#{action}\"");
        try
        {
            return await SendLimitedAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("UPnP {Action} failed: {Error}", action, ex.Message);
            return (false, string.Empty);
        }
    }

    /// <summary>
    /// Sends a request with a 5-second limit and reads at most <see cref="MaxDocumentSize"/> bytes.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Whether the status was successful and the body.</returns>
    /// <exception cref="InvalidDataException">The body is too large.</exception>
    private async Task<(bool Success, string Body)> SendLimitedAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, timeout.Token).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxDocumentSize)
            {
                throw new InvalidDataException("The router response is too large.");
            }

            buffer.Write(chunk, 0, read);
        }

        return (response.IsSuccessStatusCode, Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length));
    }
}
