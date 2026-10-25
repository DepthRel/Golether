using Golether.Core.Data.Enums;
using Golether.Core.Data.Stores;
using Golether.Session;

namespace Golether.UI.Services;

/// <summary>
/// Claps a clapperboard when somebody else writes in the chat. The sender hears nothing for their own message, and
/// reactions are silent: they float over the video and come in handfuls.
/// </summary>
public sealed class ChatSoundNotifier
{
    /// <summary>
    /// The setting that switches the sound of new messages on or off (<c>true</c> or <c>false</c>; on by default).
    /// </summary>
    public const string SETTING = "ui.chat.sound";

    /// <summary>
    /// The shortest time between two sounds: a burst of messages claps once, not in a rattle.
    /// </summary>
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The settings.
    /// </summary>
    private readonly ISettingsStore _settings;

    /// <summary>
    /// Plays the sound.
    /// </summary>
    private readonly ISoundPlayer _player;

    /// <summary>
    /// The time source.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// The sound, read once when it is first needed.
    /// </summary>
    private readonly Lazy<byte[]> _sound = new(ClapperboardSound.Load);

    /// <summary>
    /// Guards <see cref="_lastPlayed"/>; messages arrive on background threads.
    /// </summary>
    private readonly Lock _gate = new();

    /// <summary>
    /// When the sound was last played (a timestamp of <see cref="_timeProvider"/>), or <see langword="null"/>.
    /// </summary>
    private long? _lastPlayed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatSoundNotifier"/> class.
    /// </summary>
    /// <param name="session">The session service whose chat is listened to.</param>
    /// <param name="settings">The settings.</param>
    /// <param name="player">Plays the sound.</param>
    /// <param name="timeProvider">The time source.</param>
    public ChatSoundNotifier(ISessionService session, ISettingsStore settings, ISoundPlayer player, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(session);
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        session.ChatReceived += (_, entry) => OnChat(entry);
    }

    /// <summary>
    /// Gets a value indicating whether new messages are announced by the sound.
    /// </summary>
    public bool Enabled { get; private set; } = true;

    /// <summary>
    /// Reads the stored choice.
    /// </summary>
    /// <returns>A task that completes when the choice is read.</returns>
    public async Task LoadAsync() => Enabled = await _settings.GetAsync(SETTING, CancellationToken.None) != "false";

    /// <summary>
    /// Switches the sound on or off and remembers the choice.
    /// </summary>
    /// <param name="value">Whether new messages are announced.</param>
    public void SetEnabled(bool value)
    {
        if (Enabled == value)
        {
            return;
        }

        Enabled = value;
        _ = _settings.SetAsync(SETTING, value ? "true" : "false", CancellationToken.None);
    }

    /// <summary>
    /// Plays the sound for a text message of another participant, unless it is off or has just been played.
    /// </summary>
    /// <param name="entry">The message.</param>
    private void OnChat(ChatEntry entry)
    {
        if (!Enabled || entry.IsLocal || entry.Kind != ChatKind.Text)
        {
            return;
        }

        lock (_gate)
        {
            var now = _timeProvider.GetTimestamp();
            if (_lastPlayed is { } last && _timeProvider.GetElapsedTime(last, now) < MinimumInterval)
            {
                return;
            }

            _lastPlayed = now;
        }

        _player.Play(_sound.Value);
    }
}
