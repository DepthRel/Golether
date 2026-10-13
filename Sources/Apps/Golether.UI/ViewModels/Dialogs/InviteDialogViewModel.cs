using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Golether.Core.Networking;
using Golether.Localization;
using Golether.Security.Invites;
using Golether.UI.Services;

namespace Golether.UI.ViewModels.Dialogs;

/// <summary>
/// The invitation dialog: a link the guest clicks and, as the alternative, a key the guest pastes into the
/// application. Both are one-time and carry the host addresses.
/// </summary>
/// <remarks>
/// The link and the key are two separate invitations with their own tokens: the link lives for
/// <see cref="InviteRegistry.WebLinkLifetime"/>, the key for the chosen time, at most
/// <see cref="InviteRegistry.MaxLifetime"/>.
/// </remarks>
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
    /// Gets the lifetime options of the key.
    /// </summary>
    public IReadOnlyList<InviteLifetimeOption> Lifetimes { get; } =
    [
        new(Texts.Get("Invite.Lifetime.Minutes15"), TimeSpan.FromMinutes(15)),
        new(Texts.Get("Invite.Lifetime.Hour1"), InviteRegistry.MaxLifetime),
    ];

    /// <summary>
    /// Gets or sets the selected lifetime of the key.
    /// </summary>
    [ObservableProperty]
    public partial InviteLifetimeOption SelectedLifetime { get; set; }

    /// <summary>
    /// Gets or sets an additional address, for example a forwarded public address or the tunnel address.
    /// </summary>
    [ObservableProperty]
    public partial string ExtraAddress { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the web link.
    /// </summary>
    [ObservableProperty]
    public partial string Link { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the key: the invitation that is pasted into the application.
    /// </summary>
    [ObservableProperty]
    public partial string Key { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the local time until which the link works.
    /// </summary>
    [ObservableProperty]
    public partial string LinkExpires { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the local time until which the key works.
    /// </summary>
    [ObservableProperty]
    public partial string KeyExpires { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the addresses in the invitations.
    /// </summary>
    [ObservableProperty]
    public partial string AddressesText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the result of the last action.
    /// </summary>
    [ObservableProperty]
    public partial string Message { get; set; } = string.Empty;

    /// <summary>
    /// Creates a new link and a new key.
    /// </summary>
    [RelayCommand]
    public void Regenerate()
    {
        if (!TryReadExtraAddress(out var extra))
        {
            return;
        }

        if (TryCreate(extra, InviteRegistry.WebLinkLifetime) is not { } link || TryCreate(extra, SelectedLifetime.Lifetime) is not { } key)
        {
            return;
        }

        Link = link.ToWebLink();
        LinkExpires = FormatTime(link.ExpiresAt);
        ShowKey(key);
        Message = Texts.Get("Invite.Dialog.Created");
    }

    /// <summary>
    /// Makes a new key when another lifetime was chosen. The link stays: it may have been sent already.
    /// </summary>
    /// <param name="value">The chosen lifetime.</param>
    partial void OnSelectedLifetimeChanged(InviteLifetimeOption value)
    {
        // Before the first invitation there is no key to replace.
        if (Key.Length == 0 || !TryReadExtraAddress(out var extra) || TryCreate(extra, value.Lifetime) is not { } key)
        {
            return;
        }

        ShowKey(key);
        Message = Texts.Get("Invite.Dialog.KeyRenewed");
    }

    /// <summary>
    /// Reads the additional address typed by the user.
    /// </summary>
    /// <param name="extra">The address, or nothing when the field is empty.</param>
    /// <returns><see langword="false"/> when the field holds something that is not an address; the reason is shown.</returns>
    private bool TryReadExtraAddress(out List<PeerEndpoint> extra)
    {
        extra = [];
        if (string.IsNullOrWhiteSpace(ExtraAddress))
        {
            return true;
        }

        if (!PeerEndpoint.TryParse(ExtraAddress.Trim(), out var endpoint))
        {
            Message = Texts.Get("Invite.Dialog.BadAddress");
            return false;
        }

        extra.Add(endpoint);
        return true;
    }

    /// <summary>
    /// Asks the session for an invitation.
    /// </summary>
    /// <param name="extra">The additional addresses.</param>
    /// <param name="lifetime">The lifetime.</param>
    /// <returns>The invitation, or <see langword="null"/> when none could be made; the reason is shown.</returns>
    private Invite? TryCreate(IReadOnlyList<PeerEndpoint> extra, TimeSpan lifetime)
    {
        try
        {
            return _session.CreateInvite(extra, lifetime);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The session may have ended meanwhile, or the computer has no network address: the reason is shown
            // instead of ending the whole application.
            Link = string.Empty;
            Key = string.Empty;
            LinkExpires = string.Empty;
            KeyExpires = string.Empty;
            AddressesText = string.Empty;
            Message = ex.Message;
            return null;
        }
    }

    /// <summary>
    /// Shows a freshly made key.
    /// </summary>
    /// <param name="key">The invitation.</param>
    private void ShowKey(Invite key)
    {
        Key = key.ToLink();
        KeyExpires = FormatTime(key.ExpiresAt);
        AddressesText = string.Join(", ", key.Endpoints);
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

    /// <summary>
    /// Copies the key.
    /// </summary>
    /// <returns>A task that completes when the key is copied.</returns>
    [RelayCommand]
    private async Task CopyKeyAsync()
    {
        await _dialogs.CopyTextAsync(Key);
        Message = Texts.Get("Invite.Dialog.KeyCopied");
    }

    /// <summary>
    /// Formats the moment an invitation ends as the local time of day: no invitation outlives an hour.
    /// </summary>
    /// <param name="moment">The moment.</param>
    /// <returns>The time, for example <c>21:45</c>.</returns>
    private static string FormatTime(DateTimeOffset moment)
        => moment.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
}
