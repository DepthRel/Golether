using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Golether.Core.Networking;
using Golether.Localization;
using Golether.Tunnels.AmneziaWG.Configuration;
using Golether.Tunnels.AmneziaWG.Control;
using Golether.UI.Services;

namespace Golether.UI.ViewModels.Dialogs;

/// <summary>
/// The AmneziaWG dialog: the host creates an offer and completes it with the answer; a participant accepts an offer and
/// returns the answer.
/// </summary>
public sealed partial class TunnelDialogViewModel : ObservableObject
{
    /// <summary>
    /// The tunnel workflow.
    /// </summary>
    private readonly TunnelWorkflow _workflow;

    /// <summary>
    /// The dialogs.
    /// </summary>
    private readonly IDialogService _dialogs;

    /// <summary>
    /// The user name.
    /// </summary>
    private readonly string _userName;

    /// <summary>
    /// The configuration of the last host result.
    /// </summary>
    private (string Name, AwgConfiguration Configuration)? _hostConfiguration;

    /// <summary>
    /// The configuration of the last participant result.
    /// </summary>
    private (string Name, AwgConfiguration Configuration)? _participantConfiguration;

    /// <summary>
    /// Initializes a new instance of the <see cref="TunnelDialogViewModel"/> class.
    /// </summary>
    /// <param name="workflow">The tunnel workflow.</param>
    /// <param name="dialogs">The dialogs.</param>
    /// <param name="userName">The user name.</param>
    /// <param name="component">The tunnel component, so it can be installed from this dialog; null on other systems.</param>
    public TunnelDialogViewModel(TunnelWorkflow workflow, IDialogService dialogs, string userName, ComponentItemViewModel? component = null)
    {
        _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _userName = userName;
        Component = component;
        ListenPort = HostTunnelInterface.PickListenPort();

        // A tunnel of an earlier run may still be up: then the button to bring it down is there from the start.
        HasRaisedTunnels = _workflow.GetRaised().Count > 0;
    }

    /// <summary>
    /// Gets or sets the availability of the AmneziaWG tools.
    /// </summary>
    [ObservableProperty]
    public partial string AvailabilityText { get; set; } = Texts.Get("Tunnel.Dialog.Checking");

    /// <summary>
    /// Gets or sets a value indicating whether the tools are installed.
    /// </summary>
    [ObservableProperty]
    public partial bool IsAvailable { get; set; }

    /// <summary>
    /// Gets or sets the public address of this device (host:port), optional.
    /// </summary>
    [ObservableProperty]
    public partial string PublicAddress { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the created offer.
    /// </summary>
    [ObservableProperty]
    public partial string OfferText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the answer pasted by the host.
    /// </summary>
    [ObservableProperty]
    public partial string AnswerInput { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the result text of the host.
    /// </summary>
    [ObservableProperty]
    public partial string HostResult { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the host configuration is ready.
    /// </summary>
    [ObservableProperty]
    public partial bool HasHostConfiguration { get; set; }

    /// <summary>
    /// Gets or sets the offer pasted by a participant.
    /// </summary>
    [ObservableProperty]
    public partial string OfferInput { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the UDP port of the participant interface.
    /// </summary>
    [ObservableProperty]
    public partial decimal? ListenPort { get; set; }

    /// <summary>
    /// Gets or sets the created answer.
    /// </summary>
    [ObservableProperty]
    public partial string AnswerText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the result text of the participant.
    /// </summary>
    [ObservableProperty]
    public partial string ParticipantResult { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the participant configuration is ready.
    /// </summary>
    [ObservableProperty]
    public partial bool HasParticipantConfiguration { get; set; }

    /// <summary>
    /// Gets or sets the status message.
    /// </summary>
    [ObservableProperty]
    public partial string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether Golether has a tunnel up, so it can be offered to bring it down.
    /// </summary>
    [ObservableProperty]
    public partial bool HasRaisedTunnels { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether an operation runs.
    /// </summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>
    /// Checks the AmneziaWG tools.
    /// </summary>
    /// <returns>A task that completes when the check finished.</returns>
    public async Task InitializeAsync()
    {
        if (Component is { } component)
        {
            component.PropertyChanged += async (_, _) => await RefreshAvailabilityAsync();
        }

        await RefreshAvailabilityAsync();
    }

    /// <summary>
    /// Gets the tunnel component, so it can be installed right here when it is missing.
    /// </summary>
    public ComponentItemViewModel? Component { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the component has to be installed before anything can be done.
    /// </summary>
    [ObservableProperty]
    public partial bool NeedsComponent { get; set; }

    /// <summary>
    /// Re-reads whether the tunnel engine is there. Called again after the component was installed.
    /// </summary>
    /// <returns>A task that completes when the state is known.</returns>
    private async Task RefreshAvailabilityAsync()
    {
        var problem = await _workflow.CheckAvailabilityAsync(CancellationToken.None);
        IsAvailable = problem is null;
        NeedsComponent = problem is not null && Component is { IsAvailable: false };
        AvailabilityText = problem ?? Texts.Get("Tunnel.Dialog.EngineReady");
    }

    /// <summary>
    /// Creates an offer.
    /// </summary>
    /// <returns>A task that completes when the offer is ready.</returns>
    [RelayCommand]
    private Task CreateOfferAsync() => RunAsync(async () =>
    {
        OfferText = await _workflow.CreateOfferAsync(_userName, ParsePublicAddress(), CancellationToken.None);
        Message = Texts.Get("Tunnel.Dialog.OfferReady");
    });

    /// <summary>
    /// Completes the offer with the pasted answer.
    /// </summary>
    /// <returns>A task that completes when the host configuration is ready.</returns>
    [RelayCommand]
    private Task CompleteOfferAsync() => RunAsync(async () =>
    {
        var result = await _workflow.CompleteOfferAsync(AnswerInput, CancellationToken.None);
        _hostConfiguration = (result.InterfaceName, result.Configuration);
        HasHostConfiguration = true;
        HostResult = Texts.Format("Tunnel.Dialog.ParticipantAdded", result.ParticipantName, result.VerificationCode);
        Message = Texts.Get("Tunnel.Dialog.RaiseAgain");
    });

    /// <summary>
    /// Accepts the pasted offer.
    /// </summary>
    /// <returns>A task that completes when the answer is ready.</returns>
    [RelayCommand]
    private Task AcceptOfferAsync() => RunAsync(async () =>
    {
        var result = await _workflow.AcceptOfferAsync(OfferInput, _userName, ParsePublicAddress(), (int)(ListenPort ?? 51820), CancellationToken.None);
        _participantConfiguration = (result.InterfaceName, result.Configuration);
        HasParticipantConfiguration = true;
        AnswerText = result.AnswerText;
        ParticipantResult = Texts.Format("Tunnel.Dialog.TunnelTo", result.HostName, result.VerificationCode, result.HostAddress);
        Message = Texts.Format("Tunnel.Dialog.SendAnswer", result.HostAddress);
    });

    /// <summary>
    /// Copies the offer.
    /// </summary>
    /// <returns>A task that completes when the text is copied.</returns>
    [RelayCommand]
    private Task CopyOfferAsync() => CopyAsync(OfferText);

    /// <summary>
    /// Copies the answer.
    /// </summary>
    /// <returns>A task that completes when the text is copied.</returns>
    [RelayCommand]
    private Task CopyAnswerAsync() => CopyAsync(AnswerText);

    /// <summary>
    /// Pastes the answer.
    /// </summary>
    /// <returns>A task that completes when the text is pasted.</returns>
    [RelayCommand]
    private async Task PasteAnswerAsync() => AnswerInput = await _dialogs.PasteTextAsync() ?? AnswerInput;

    /// <summary>
    /// Pastes the offer.
    /// </summary>
    /// <returns>A task that completes when the text is pasted.</returns>
    [RelayCommand]
    private async Task PasteOfferAsync() => OfferInput = await _dialogs.PasteTextAsync() ?? OfferInput;

    /// <summary>
    /// Brings the host tunnel up.
    /// </summary>
    /// <returns>A task that completes when the tunnel is up.</returns>
    [RelayCommand]
    private Task ApplyHostAsync() => ApplyAsync(_hostConfiguration);

    /// <summary>
    /// Brings the participant tunnel up.
    /// </summary>
    /// <returns>A task that completes when the tunnel is up.</returns>
    [RelayCommand]
    private Task ApplyParticipantAsync() => ApplyAsync(_participantConfiguration);

    /// <summary>
    /// Exports the host configuration.
    /// </summary>
    /// <returns>A task that completes when the file is written.</returns>
    [RelayCommand]
    private Task ExportHostAsync() => ExportAsync(_hostConfiguration);

    /// <summary>
    /// Exports the participant configuration.
    /// </summary>
    /// <returns>A task that completes when the file is written.</returns>
    [RelayCommand]
    private Task ExportParticipantAsync() => ExportAsync(_participantConfiguration);

    /// <summary>
    /// Parses the optional public address.
    /// </summary>
    /// <returns>The address list.</returns>
    /// <exception cref="FormatException">The address is malformed.</exception>
    private IReadOnlyList<PeerEndpoint> ParsePublicAddress()
    {
        if (string.IsNullOrWhiteSpace(PublicAddress))
        {
            return [];
        }

        return PeerEndpoint.TryParse(PublicAddress.Trim(), out var endpoint)
            ? [endpoint]
            : throw new FormatException(Texts.Get("Tunnel.Dialog.BadPublicAddress"));
    }

    /// <summary>
    /// Copies text.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>A task that completes when the text is copied.</returns>
    private async Task CopyAsync(string text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            await _dialogs.CopyTextAsync(text);
            Message = Texts.Get("Tunnel.Dialog.Copied");
        }
    }

    /// <summary>
    /// Brings a tunnel up.
    /// </summary>
    /// <param name="target">The interface and configuration.</param>
    /// <returns>A task that completes when the tunnel is up.</returns>
    private Task ApplyAsync((string Name, AwgConfiguration Configuration)? target) => RunAsync(async () =>
    {
        if (target is not { } value)
        {
            return;
        }

        await _workflow.ApplyAsync(value.Name, value.Configuration, CancellationToken.None);
        HasRaisedTunnels = true;
        Message = Texts.Format("Tunnel.Dialog.Raised", value.Name);
    });

    /// <summary>
    /// Brings down the tunnels this application raised, without waiting for it to close.
    /// </summary>
    /// <returns>A task that completes when the tunnels are down.</returns>
    [RelayCommand]
    private Task DropAsync() => RunAsync(async () =>
    {
        var remaining = await _workflow.DropRaisedAsync(CancellationToken.None);
        HasRaisedTunnels = remaining.Count > 0;
        Message = remaining.Count == 0
            ? Texts.Get("Tunnel.Dialog.Dropped")
            : Texts.Format("Tunnel.Dialog.StillRaised", string.Join(", ", remaining));
    });

    /// <summary>
    /// Exports a configuration file.
    /// </summary>
    /// <param name="target">The interface and configuration.</param>
    /// <returns>A task that completes when the file is written.</returns>
    private Task ExportAsync((string Name, AwgConfiguration Configuration)? target) => RunAsync(async () =>
    {
        if (target is not { } value)
        {
            return;
        }

        var path = await _dialogs.PickConfigSavePathAsync(value.Name + ".conf");
        if (path is not null)
        {
            await _workflow.ExportAsync(value.Configuration, path, CancellationToken.None);
            Message = Texts.Format("Tunnel.Dialog.ConfigSaved", path);
        }
    });

    /// <summary>
    /// Runs an operation and reports failures in <see cref="Message"/>.
    /// </summary>
    /// <param name="operation">The operation.</param>
    /// <returns>A task that completes when the operation finished.</returns>
    private async Task RunAsync(Func<Task> operation)
    {
        IsBusy = true;
        try
        {
            await operation();
        }
        catch (Exception ex) when (ex is FormatException or TunnelControlException or InvalidOperationException or IOException or UnauthorizedAccessException or CryptographicException)
        {
            Message = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
