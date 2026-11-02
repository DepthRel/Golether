using System.Globalization;
using Golether.Core.Data.Stores;
using Golether.Core.Identity;

namespace Golether.Client.Services;

/// <summary>
/// Remembers how loud this device plays the voice of each participant, for later sessions. A slider moves in many small
/// steps, so a volume is written only after it has stopped.
/// </summary>
public sealed class VoiceVolumeStore
{
    /// <summary>
    /// The prefix of the settings with the volumes: the identifier of the participant follows it.
    /// </summary>
    public const string SETTING_PREFIX = "voice.volume.";

    /// <summary>
    /// The settings.
    /// </summary>
    private readonly ISettingsStore _settings;

    /// <summary>
    /// How long a volume must stay unchanged to be written.
    /// </summary>
    private readonly TimeSpan _saveDelay;

    /// <summary>
    /// The number of the latest change; a newer change replaces the ones that wait.
    /// </summary>
    private long _version;

    /// <summary>
    /// Initializes a new instance of the <see cref="VoiceVolumeStore"/> class.
    /// </summary>
    /// <param name="settings">The settings.</param>
    /// <param name="saveDelay">How long a volume must stay unchanged to be written.</param>
    public VoiceVolumeStore(ISettingsStore settings, TimeSpan saveDelay)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _saveDelay = saveDelay;
    }

    /// <summary>
    /// Writes a volume once the slider has stopped.
    /// </summary>
    /// <param name="peer">The participant.</param>
    /// <param name="percent">The volume in percent.</param>
    /// <returns>A task that completes when the value is written or replaced by a newer one.</returns>
    public async Task SaveAsync(PeerId peer, double percent)
    {
        var version = Interlocked.Increment(ref _version);
        await Task.Delay(_saveDelay).ConfigureAwait(false);

        if (Interlocked.Read(ref _version) == version)
        {
            await _settings.SetAsync(SETTING_PREFIX + peer.Value, percent.ToString(CultureInfo.InvariantCulture), CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Reads the volume of a participant.
    /// </summary>
    /// <param name="peer">The participant.</param>
    /// <returns>The volume in percent, or <see langword="null"/> when none is stored.</returns>
    public async Task<double?> ReadAsync(PeerId peer)
    {
        var stored = await _settings.GetAsync(SETTING_PREFIX + peer.Value, CancellationToken.None);

        return double.TryParse(stored, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent) ? percent : null;
    }
}
