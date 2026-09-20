using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Golether.Core.Data.Entities;
using Golether.Core.Data.Stores;
using Golether.Core.Networking;
using Golether.Security.Identity;
using Golether.Security.Secrets;
using Golether.Session;
using Golether.Transports.Relay;
using Golether.Tunnels.AmneziaWG.Configuration;
using Golether.Tunnels.AmneziaWG.Control;
using Golether.Tunnels.AmneziaWG.Packages;

namespace Golether.UI.Services;

/// <summary>
/// Persists and applies the AmneziaWG offer/answer flow.
/// </summary>
public sealed class TunnelWorkflow
{
    /// <summary>
    /// The name of the host interface.
    /// </summary>
    public const string HostInterfaceName = "golether0";

    /// <summary>
    /// The JSON options of stored secrets.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The local device.
    /// </summary>
    private readonly DeviceIdentity _identity;

    /// <summary>
    /// The tunnel store.
    /// </summary>
    private readonly ITunnelStore _store;

    /// <summary>
    /// Protects stored secrets.
    /// </summary>
    private readonly ISecretProtector _protector;

    /// <summary>
    /// Brings tunnels up.
    /// </summary>
    private readonly ITunnelController _controller;

    /// <summary>
    /// The time provider.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// The negotiator.
    /// </summary>
    private readonly TunnelNegotiator _negotiator;

    /// <summary>
    /// Initializes a new instance of the <see cref="TunnelWorkflow"/> class.
    /// </summary>
    /// <param name="identity">The local device.</param>
    /// <param name="store">The tunnel store.</param>
    /// <param name="protector">Protects stored secrets.</param>
    /// <param name="controller">Brings tunnels up.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="stunServers">
    /// The STUN servers asked for the public address, <see langword="null"/> for the usual ones, or an empty list to
    /// ask nobody (tests, and users who would rather not touch a third-party server).
    /// </param>
    /// <param name="raisedStatePath">
    /// The file that remembers which tunnels this application raised, so they can be brought down at the end even
    /// after a crash; <see langword="null"/> keeps the list in memory only.
    /// </param>
    public TunnelWorkflow(
        DeviceIdentity identity,
        ITunnelStore store,
        ISecretProtector protector,
        ITunnelController controller,
        TimeProvider timeProvider,
        IReadOnlyList<string>? stunServers = null,
        string? raisedStatePath = null)
    {
        _raisedStatePath = raisedStatePath;
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _stunServers = stunServers;
        _negotiator = new TunnelNegotiator(identity, timeProvider);
    }

    /// <summary>
    /// The STUN servers, or <see langword="null"/> for the usual ones.
    /// </summary>
    private readonly IReadOnlyList<string>? _stunServers;

    /// <summary>
    /// The file with the names of the tunnels this application raised, or <see langword="null"/> to keep them in
    /// memory only (tests).
    /// </summary>
    private readonly string? _raisedStatePath;

    /// <summary>
    /// The names of the raised tunnels, mirroring the file.
    /// </summary>
    private readonly List<string> _raised = [];

    /// <summary>
    /// Checks whether the AmneziaWG tools are installed.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="null"/> when available, otherwise the reason.</returns>
    public Task<string?> CheckAvailabilityAsync(CancellationToken cancellationToken) => _controller.CheckAvailabilityAsync(cancellationToken);

    /// <summary>
    /// Creates an offer and remembers its secrets.
    /// </summary>
    /// <param name="hostName">The host name.</param>
    /// <param name="publicEndpoints">Addresses entered by the user (for example the forwarded public address).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The offer package text.</returns>
    public async Task<string> CreateOfferAsync(string hostName, IReadOnlyList<PeerEndpoint> publicEndpoints, CancellationToken cancellationToken)
    {
        var hostInterface = await GetOrCreateHostInterfaceAsync(cancellationToken).ConfigureAwait(false);
        var used = (await _store.ListActiveAsync(cancellationToken).ConfigureAwait(false))
            .Where(t => t.Role == TunnelRole.Host)
            .Select(t => t.TunnelAddress)
            .ToArray();
        var entered = publicEndpoints.Select(e => e with { Port = hostInterface.ListenPort }).ToList();
        if (await FindPublicEndpointAsync(hostInterface.ListenPort, cancellationToken).ConfigureAwait(false) is { } seen)
        {
            // The address the routers of the world see: without it a participant from another network has nothing
            // to aim at, and the user would have to forward a port by hand.
            entered.Add(seen);
        }

        var endpoints = EndpointDiscovery.Discover(hostInterface.ListenPort, entered)
            .Where(e => !e.Host.StartsWith(hostInterface.SubnetBase[..hostInterface.SubnetBase.LastIndexOf('.')], StringComparison.Ordinal))
            .Take(8)
            .ToArray();
        var offer = _negotiator.CreateOffer(hostInterface, hostName, endpoints, used);
        await _store.AddAsync(new TunnelEntity
        {
            OfferId = offer.Secrets.OfferId,
            Role = TunnelRole.Host,
            Status = TunnelStatus.Pending,
            InterfaceName = HostInterfaceName,
            TunnelAddress = offer.Secrets.AssignedAddress,
            SecretData = Protect(offer.Secrets),
            ExpiresAt = offer.Secrets.ExpiresAt,
        }, cancellationToken).ConfigureAwait(false);
        return offer.PackageText;
    }

    /// <summary>
    /// Completes an offer with the answer of the participant.
    /// </summary>
    /// <param name="answerText">The answer package.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result.</returns>
    /// <exception cref="FormatException">The answer is invalid or unknown.</exception>
    public async Task<HostTunnelResult> CompleteOfferAsync(string answerText, CancellationToken cancellationToken)
    {
        var answer = TunnelPackageCodec.Decode<TunnelAnswerBody>(TunnelPackageCodec.AnswerKind, answerText, b => b.ParticipantCertificate);
        var record = await _store.FindByOfferAsync(TunnelRole.Host, answer.Body.OfferId, cancellationToken).ConfigureAwait(false);
        if (record is null || record.Status != TunnelStatus.Pending || record.SecretData is null)
        {
            throw new FormatException("Ответ относится к неизвестному или уже использованному предложению.");
        }

        var secrets = Unprotect<PendingOfferSecrets>(record.SecretData);
        var completion = _negotiator.CompleteOffer(secrets, answerText);

        // The other side pokes this router at the same moment; poking back from the port of the host interface is
        // what makes both routers let the handshake through.
        var hostInterface = await GetOrCreateHostInterfaceAsync(cancellationToken).ConfigureAwait(false);
        await PunchAsync(hostInterface.ListenPort, answer.Body.ParticipantEndpoints, cancellationToken).ConfigureAwait(false);
        record.Status = TunnelStatus.Ready;
        record.PeerId = completion.ParticipantPeerId.Value;
        record.PeerName = completion.ParticipantName;
        record.SecretData = Protect(completion.Peer);
        record.ExpiresAt = null;
        await _store.UpdateAsync(record, cancellationToken).ConfigureAwait(false);

        return new HostTunnelResult(completion.ParticipantName, completion.VerificationCode, HostInterfaceName,
            await BuildHostConfigurationAsync(cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Accepts an offer of a host.
    /// </summary>
    /// <param name="offerText">The offer package.</param>
    /// <param name="participantName">The participant name.</param>
    /// <param name="publicEndpoints">Addresses entered by the user.</param>
    /// <param name="listenPort">The UDP port of the participant interface.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The result.</returns>
    /// <exception cref="FormatException">The offer is invalid.</exception>
    public async Task<ParticipantTunnelResult> AcceptOfferAsync(
        string offerText,
        string participantName,
        IReadOnlyList<PeerEndpoint> publicEndpoints,
        int listenPort,
        CancellationToken cancellationToken)
    {
        var entered = publicEndpoints.ToList();
        if (await FindPublicEndpointAsync(listenPort, cancellationToken).ConfigureAwait(false) is { } seen)
        {
            entered.Add(seen);
        }

        var acceptance = _negotiator.AcceptOffer(offerText, participantName, entered, listenPort);
        var interfaceName = "glt-" + acceptance.HostPeerId.Value[..8];
        var body = TunnelPackageCodec.Decode<TunnelOfferBody>(TunnelPackageCodec.OfferKind, offerText, b => b.HostCertificate).Body;

        // The routers on both sides only let in what was asked for. Poking every address of the host from the port
        // the tunnel will use leaves a hole in this router, so the answer of the host is not dropped.
        await PunchAsync(listenPort, body.HostEndpoints, cancellationToken).ConfigureAwait(false);
        var offerId = body.OfferId;
        var existing = await _store.FindByOfferAsync(TunnelRole.Participant, offerId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            throw new FormatException("Это предложение уже принято. Попросите ведущего создать новое.");
        }

        await _store.AddAsync(new TunnelEntity
        {
            OfferId = offerId,
            Role = TunnelRole.Participant,
            Status = TunnelStatus.Ready,
            InterfaceName = interfaceName,
            TunnelAddress = acceptance.HostAddress,
            PeerId = acceptance.HostPeerId.Value,
            PeerName = acceptance.HostName,
            SecretData = _protector.Protect(Encoding.UTF8.GetBytes(acceptance.Configuration.Render())),
        }, cancellationToken).ConfigureAwait(false);

        return new ParticipantTunnelResult(
            acceptance.AnswerText,
            acceptance.HostName,
            acceptance.HostAddress,
            acceptance.VerificationCode,
            interfaceName,
            acceptance.Configuration);
    }

    /// <summary>
    /// Builds the host configuration with all established participants.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The configuration.</returns>
    public async Task<AwgConfiguration> BuildHostConfigurationAsync(CancellationToken cancellationToken)
    {
        var hostInterface = await GetOrCreateHostInterfaceAsync(cancellationToken).ConfigureAwait(false);
        var peers = (await _store.ListActiveAsync(cancellationToken).ConfigureAwait(false))
            .Where(t => t.Role == TunnelRole.Host && t.Status == TunnelStatus.Ready && t.SecretData is not null)
            .Select(t => Unprotect<AwgPeer>(t.SecretData!))
            .ToArray();
        return hostInterface.BuildConfiguration(peers);
    }

    /// <summary>
    /// Brings a tunnel up.
    /// </summary>
    /// <param name="interfaceName">The interface name.</param>
    /// <param name="configuration">The configuration.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the tunnel is up.</returns>
    public async Task ApplyAsync(string interfaceName, AwgConfiguration configuration, CancellationToken cancellationToken)
    {
        await _controller.UpAsync(interfaceName, configuration, cancellationToken).ConfigureAwait(false);
        Remember(interfaceName);
    }

    /// <summary>
    /// Brings down every tunnel this application raised and forgets them. Called when Golether closes, and once at
    /// the start for tunnels left behind by a run that ended badly.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The interfaces that are still up, empty when everything went down.</returns>
    /// <remarks>
    /// Bringing a tunnel down needs administrator rights, so the user is asked. A refusal is not an error: the
    /// tunnel simply stays, and its name is kept so the next run can offer again.
    /// </remarks>
    public async Task<IReadOnlyList<string>> DropRaisedAsync(CancellationToken cancellationToken)
    {
        var remaining = new List<string>();
        foreach (var name in ReadRaised())
        {
            try
            {
                await _controller.DownAsync(name, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is TunnelControlException or TimeoutException or OperationCanceledException)
            {
                Trace.WriteLine($"Golether tunnel {name} stayed up: {ex.Message}");
                remaining.Add(name);
            }
        }

        WriteRaised(remaining);
        return remaining;
    }

    /// <summary>
    /// Gets the tunnels this application raised and has not brought down yet.
    /// </summary>
    /// <returns>The interface names.</returns>
    public IReadOnlyList<string> GetRaised() => ReadRaised();

    /// <summary>
    /// Notes that a tunnel is up, so it can be brought down later even after a crash.
    /// </summary>
    /// <param name="interfaceName">The interface name.</param>
    private void Remember(string interfaceName)
    {
        var names = ReadRaised().ToList();
        if (!names.Contains(interfaceName, StringComparer.OrdinalIgnoreCase))
        {
            names.Add(interfaceName);
            WriteRaised(names);
        }
    }

    /// <summary>
    /// Reads the noted tunnels.
    /// </summary>
    /// <returns>The interface names.</returns>
    private IReadOnlyList<string> ReadRaised()
    {
        if (_raisedStatePath is null)
        {
            return _raised.ToArray();
        }

        try
        {
            return File.Exists(_raisedStatePath)
                ? File.ReadAllLines(_raisedStatePath).Where(line => !string.IsNullOrWhiteSpace(line)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return _raised.ToArray();
        }
    }

    /// <summary>
    /// Writes the noted tunnels.
    /// </summary>
    /// <param name="names">The interface names.</param>
    private void WriteRaised(IReadOnlyList<string> names)
    {
        _raised.Clear();
        _raised.AddRange(names);
        if (_raisedStatePath is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_raisedStatePath)!);
            File.WriteAllLines(_raisedStatePath, names);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.WriteLine($"Golether raised tunnels not written: {ex.Message}");
        }
    }

    /// <summary>
    /// Writes a configuration file for AmneziaVPN or <c>awg-quick</c>.
    /// </summary>
    /// <param name="configuration">The configuration.</param>
    /// <param name="path">The target file.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the file is written.</returns>
    public Task ExportAsync(AwgConfiguration configuration, string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        SecretProtectors.WriteOwnerOnlyFile(path, Encoding.UTF8.GetBytes(configuration.Render()));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Loads the host interface or creates and stores a new one.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The host interface.</returns>
    private async Task<HostTunnelInterface> GetOrCreateHostInterfaceAsync(CancellationToken cancellationToken)
    {
        var stored = await _store.GetHostInterfaceAsync(HostInterfaceName, cancellationToken).ConfigureAwait(false);
        if (stored is not null)
        {
            return Unprotect<HostTunnelInterface>(stored);
        }

        var created = HostTunnelInterface.Create(HostInterfaceName);
        await _store.SaveHostInterfaceAsync(HostInterfaceName, Protect(created), cancellationToken).ConfigureAwait(false);
        return created;
    }

    /// <summary>
    /// Asks the public STUN servers how a UDP port of this device looks from outside, so the address can go into a
    /// package the other side will aim at.
    /// </summary>
    /// <param name="port">The port the tunnel listens on.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The address, or <see langword="null"/> when no server answered, the port is already taken by a running
    /// tunnel, or the router hands out a different port per destination (then the address is useless anyway).
    /// </returns>
    private async Task<PeerEndpoint?> FindPublicEndpointAsync(int port, CancellationToken cancellationToken)
    {
        if (_stunServers is { Count: 0 })
        {
            return null;
        }

        try
        {
            using var socket = NatTraversal.Open(port);
            var found = await NatTraversal.DiscoverAsync(socket, _stunServers, cancellationToken).ConfigureAwait(false);
            return found is { IsPredictable: true }
                ? new PeerEndpoint(found.Endpoint.Address.ToString(), port)
                : null;
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            // The tunnel already holds the port, or there is no network: the package goes out without the address.
            Trace.WriteLine($"Golether public address for {port}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Pokes the addresses of the other side from the port the tunnel will use, then frees the port for the tunnel.
    /// </summary>
    /// <param name="port">The port the tunnel listens on.</param>
    /// <param name="targets">The addresses of the other side as <c>host:port</c>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the packets are away.</returns>
    private static async Task PunchAsync(int port, IEnumerable<string> targets, CancellationToken cancellationToken)
    {
        var addresses = new List<IPEndPoint>();
        foreach (var target in targets)
        {
            if (PeerEndpoint.TryParse(target, out var endpoint) && IPAddress.TryParse(endpoint.Host, out var address)
                && address.AddressFamily == AddressFamily.InterNetwork)
            {
                addresses.Add(new IPEndPoint(address, endpoint.Port));
            }
        }

        if (addresses.Count == 0)
        {
            return;
        }

        try
        {
            using var socket = NatTraversal.Open(port);
            await NatTraversal.PunchAsync(socket, addresses, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            Trace.WriteLine($"Golether punch from {port}: {ex.Message}");
        }
    }

    /// <summary>
    /// Serializes and protects a value.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="value">The value.</param>
    /// <returns>The protected data.</returns>
    private byte[] Protect<T>(T value) => _protector.Protect(JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions));

    /// <summary>
    /// Restores a protected value.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="data">The protected data.</param>
    /// <returns>The value.</returns>
    private T Unprotect<T>(byte[] data)
        => JsonSerializer.Deserialize<T>(_protector.Unprotect(data), JsonOptions)
           ?? throw new InvalidDataException("A stored tunnel record is empty.");
}
