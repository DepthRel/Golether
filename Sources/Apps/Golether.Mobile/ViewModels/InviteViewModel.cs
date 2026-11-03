using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Golether.Localization;
using Golether.Mobile.Services;
using Golether.Security.Invites;

namespace Golether.Mobile.ViewModels;

/// <summary>
/// The invitation into the session the phone hosts: the link that opens the application, and the key pasted into it
/// when the link does not open it.
/// </summary>
public sealed partial class InviteViewModel : ObservableObject
{
    /// <summary>
    /// Makes an invitation for the given time.
    /// </summary>
    private readonly Func<TimeSpan, Invite> _create;

    /// <summary>
    /// The system: the clipboard and the sharing.
    /// </summary>
    private readonly IMobilePlatform _platform;

    /// <summary>
    /// Initializes a new instance of the <see cref="InviteViewModel"/> class with a fresh link and key.
    /// </summary>
    /// <param name="create">Makes an invitation for the given time.</param>
    /// <param name="platform">The system.</param>
    public InviteViewModel(Func<TimeSpan, Invite> create, IMobilePlatform platform)
    {
        _create = create ?? throw new ArgumentNullException(nameof(create));
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));

        Regenerate();
        Message = string.Empty;
    }

    /// <summary>
    /// Gets or sets the web link, empty when no invitation could be made.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInvite))]
    public partial string Link { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the key: the invitation that is pasted into the application.
    /// </summary>
    [ObservableProperty]
    public partial string Key { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets what the link is for and until when it works.
    /// </summary>
    [ObservableProperty]
    public partial string LinkNote { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets what the key is for and until when it works.
    /// </summary>
    [ObservableProperty]
    public partial string KeyNote { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the result of the last action, or why no invitation could be made.
    /// </summary>
    [ObservableProperty]
    public partial string Message { get; set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether there is an invitation to give.
    /// </summary>
    public bool HasInvite => Link.Length > 0;

    /// <summary>
    /// Makes a new link and a new key; the earlier ones stay valid until they expire.
    /// </summary>
    [RelayCommand]
    private void Regenerate()
    {
        try
        {
            var link = _create(InviteRegistry.WebLinkLifetime);
            var key = _create(InviteRegistry.MaxLifetime);

            Link = link.ToWebLink();
            LinkNote = Texts.Format("Invite.Dialog.LinkNote", FormatTime(link.ExpiresAt));
            Key = key.ToLink();
            KeyNote = Texts.Format("Invite.Dialog.KeyNote", FormatTime(key.ExpiresAt));
            Message = Texts.Get("Invite.Dialog.Created");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            // The phone has no network, or the session ended meanwhile: the reason is shown.
            Link = string.Empty;
            Key = string.Empty;
            LinkNote = string.Empty;
            KeyNote = string.Empty;
            Message = ex.Message;
        }
    }

    /// <summary>
    /// Copies the link.
    /// </summary>
    [RelayCommand]
    private void CopyLink()
    {
        _platform.CopyText(Link);
        Message = Texts.Get("Invite.Dialog.Copied");
    }

    /// <summary>
    /// Copies the key.
    /// </summary>
    [RelayCommand]
    private void CopyKey()
    {
        _platform.CopyText(Key);
        Message = Texts.Get("Invite.Dialog.KeyCopied");
    }

    /// <summary>
    /// Sends the link through another application of the phone.
    /// </summary>
    [RelayCommand]
    private void ShareLink() => _platform.ShareText(Link);

    /// <summary>
    /// Formats the moment an invitation ends as the local time of day: no invitation outlives an hour.
    /// </summary>
    /// <param name="moment">The moment.</param>
    /// <returns>The time, for example <c>21:45</c>.</returns>
    private static string FormatTime(DateTimeOffset moment)
        => moment.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
}
