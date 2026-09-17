using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Golether.Core.Networking;
using Golether.UI.Services;

namespace Golether.UI.ViewModels.Dialogs;

/// <summary>
/// A lifetime option of an invitation.
/// </summary>
/// <param name="Title">The caption.</param>
/// <param name="Lifetime">The lifetime.</param>
public sealed record InviteLifetimeOption(string Title, TimeSpan Lifetime)
{
    /// <summary>
    /// Returns the caption.
    /// </summary>
    /// <returns>The caption.</returns>
    public override string ToString() => Title;
}

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
        new("15 минут", TimeSpan.FromMinutes(15)),
        new("1 час", TimeSpan.FromHours(1)),
        new("24 часа", TimeSpan.FromHours(24)),
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
                Message = "Адрес указывается как хост:порт, например 203.0.113.24:47800.";
                return;
            }

            extra.Add(endpoint);
        }

        var invite = _session.CreateInvite(extra, SelectedLifetime.Lifetime);
        Link = invite.ToLink();
        AddressesText = string.Join(", ", invite.Endpoints);
        ExpiresText = "Действует до " + invite.ExpiresAt.ToLocalTime().ToString("dd.MM HH:mm", CultureInfo.InvariantCulture) + ", одно подключение";
        Message = "Новая ссылка создана. Прежние остаются в силе до истечения срока.";
    }

    /// <summary>
    /// Copies the link.
    /// </summary>
    /// <returns>A task that completes when the link is copied.</returns>
    [RelayCommand]
    private async Task CopyAsync()
    {
        await _dialogs.CopyTextAsync(Link);
        Message = "Ссылка скопирована. Отправьте её участнику любым способом.";
    }
}
