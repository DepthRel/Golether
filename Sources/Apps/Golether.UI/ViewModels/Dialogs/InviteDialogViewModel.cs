using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Golether.Core.Networking;
using Golether.Localization;
using Golether.UI.Services;

namespace Golether.UI.ViewModels.Dialogs;

/// <summary>
/// The invitation dialog: a one-time link with the host addresses.
/// </summary>
public sealed partial class InviteDialogViewModel : ObservableObject
{
    /// <summary>
    /// The session service.
    /// </summary>
    private readonly ISessionService _session;

    /// <summary>
    /// The dialogs.
    /// </summary>
    private readonly IDialogService _dialogs;

    /// <summary>
    /// Initializes a new instance of the <see cref="InviteDialogViewModel"/> class.
    /// </summary>
    /// <param name="session">The session service.</param>
    /// <param name="dialogs">The dialogs.</param>
    public InviteDialogViewModel(ISessionService session, IDialogService dialogs)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        SelectedLifetime = Lifetimes[0];
    }

    /// <summary>
    /// Gets the lifetime options.
    /// </summary>
    public IReadOnlyList<InviteLifetimeOption> Lifetimes { get; } =
    [
        new(Texts.Get("Invite.Lifetime.Minutes15"), TimeSpan.FromMinutes(15)),
        new(Texts.Get("Invite.Lifetime.Hour1"), TimeSpan.FromHours(1)),
        new(Texts.Get("Invite.Lifetime.Hours24"), TimeSpan.FromHours(24)),
    ];

    /// <summary>
    /// Gets or sets the selected lifetime.
    /// </summary>
    [ObservableProperty]
    public partial InviteLifetimeOption SelectedLifetime { get; set; }

    /// <summary>
    /// Gets or sets an additional address, for example a forwarded public address or the tunnel address.
    /// </summary>
    [ObservableProperty]
    public partial string ExtraAddress { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the link.
    /// </summary>
    [ObservableProperty]
    public partial string Link { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the addresses in the link.
    /// </summary>
    [ObservableProperty]
    public partial string AddressesText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the expiry text.
    /// </summary>
    [ObservableProperty]
    public partial string ExpiresText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the result of the last action.
    /// </summary>
    [ObservableProperty]
    public partial string Message { get; set; } = string.Empty;

    /// <summary>
    /// Creates a new link.
    /// </summary>
    [RelayCommand]
    public void Regenerate()
    {
        var extra = new List<PeerEndpoint>();
        if (!string.IsNullOrWhiteSpace(ExtraAddress))
        {
            if (!PeerEndpoint.TryParse(ExtraAddress.Trim(), out var endpoint))
            {
                Message = Texts.Get("Invite.Dialog.BadAddress");
                return;
            }

            extra.Add(endpoint);
        }

        var invite = _session.CreateInvite(extra, SelectedLifetime.Lifetime);
        Link = invite.ToLink();
        AddressesText = string.Join(", ", invite.Endpoints);
        ExpiresText = Texts.Format("Invite.Dialog.ValidUntil", invite.ExpiresAt.ToLocalTime().ToString("dd.MM HH:mm", CultureInfo.InvariantCulture));
        Message = Texts.Get("Invite.Dialog.Created");
    }

    /// <summary>
    /// Copies the link.
    /// </summary>
    /// <returns>A task that completes when the link is copied.</returns>
    [RelayCommand]
    private async Task CopyAsync()
    {
        await _dialogs.CopyTextAsync(Link);
        Message = Texts.Get("Invite.Dialog.Copied");
    }
}
