using System.Text;
using System.Text.Json;
using Golether.Core.Data.Entities;
using Golether.Core.Data.Stores;
using Golether.Core.Networking;
using Golether.Security.Identity;
using Golether.Security.Secrets;
using Golether.Security.Verification;
using Golether.Session;
using Golether.Tunnels.AmneziaWG.Configuration;
using Golether.Tunnels.AmneziaWG.Control;
using Golether.Tunnels.AmneziaWG.Packages;

namespace Golether.UI.Services;

/// <summary>
/// The host side of a completed tunnel negotiation.
/// </summary>
/// <param name="ParticipantName">The participant name.</param>
/// <param name="VerificationCode">The code to compare by voice.</param>
/// <param name="InterfaceName">The host interface name.</param>
/// <param name="Configuration">The host configuration with all participants.</param>
public sealed record HostTunnelResult(string ParticipantName, VerificationCode VerificationCode, string InterfaceName, AwgConfiguration Configuration);

/// <summary>
/// The participant side of a tunnel negotiation.
/// </summary>
/// <param name="AnswerText">The answer package for the host.</param>
/// <param name="HostName">The host name.</param>
/// <param name="HostAddress">The host tunnel address (use it in the invitation instead of the public address).</param>
/// <param name="VerificationCode">The code to compare by voice.</param>
/// <param name="InterfaceName">The participant interface name.</param>
/// <param name="Configuration">The participant configuration.</param>
public sealed record ParticipantTunnelResult(
    string AnswerText,
    string HostName,
    string HostAddress,
    VerificationCode VerificationCode,
    string InterfaceName,
    AwgConfiguration Configuration);

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
    public TunnelWorkflow(DeviceIdentity identity, ITunnelStore store, ISecretProtector protector, ITunnelController controller, TimeProvider timeProvider)
    {
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _negotiator = new TunnelNegotiator(identity, timeProvider);
    }

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
        var endpoints = EndpointDiscovery.Discover(hostInterface.ListenPort, publicEndpoints.Select(e => e with { Port = hostInterface.ListenPort }))
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
        var acceptance = _negotiator.AcceptOffer(offerText, participantName, publicEndpoints, listenPort);
        var interfaceName = "glt-" + acceptance.HostPeerId.Value[..8];
        var offerId = TunnelPackageCodec.Decode<TunnelOfferBody>(TunnelPackageCodec.OfferKind, offerText, b => b.HostCertificate).Body.OfferId;
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
    public Task ApplyAsync(string interfaceName, AwgConfiguration configuration, CancellationToken cancellationToken)
        => _controller.UpAsync(interfaceName, configuration, cancellationToken);

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
