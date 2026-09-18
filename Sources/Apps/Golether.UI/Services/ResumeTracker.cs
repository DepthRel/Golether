using System.Globalization;
using Golether.Core.Data.Stores;

namespace Golether.UI.Services;

/// <summary>
/// Remembers where each film was stopped on this device, so the next session can continue from there.
/// </summary>
/// <remarks>
/// Positions are keyed by the media identifier (the quick hash), so a renamed copy of the same file is recognized.
/// Nothing leaves the device. A film watched to the end is forgotten.
/// </remarks>
public sealed class ResumeTracker
{
    /// <summary>
    /// The prefix of the settings with positions.
    /// </summary>
    public const string SettingPrefix = "resume.";

    /// <summary>
    /// Positions before this are not worth offering.
    /// </summary>
    public static readonly TimeSpan MinimumPosition = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Positions this close to the end count as "watched".
    /// </summary>
    public static readonly TimeSpan EndMargin = TimeSpan.FromMinutes(3);

    /// <summary>
    /// How often the position is saved during playback.
    /// </summary>
    public static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The settings.
    /// </summary>
    private readonly ISettingsStore _settings;

    /// <summary>
    /// The media whose stored position was read, so saving may start.
    /// </summary>
    private string? _mediaId;

    /// <summary>
    /// The last saved value.
    /// </summary>
    private TimeSpan? _saved;

    /// <summary>
    /// When the value was last saved.
    /// </summary>
    private DateTimeOffset _savedAt = DateTimeOffset.MinValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResumeTracker"/> class.
    /// </summary>
    /// <param name="settings">The settings.</param>
    public ResumeTracker(ISettingsStore settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <summary>
    /// Reads the stored position of a film and starts tracking it.
    /// </summary>
    /// <param name="mediaId">The media identifier.</param>
    /// <param name="duration">The duration, when known.</param>
    /// <returns>The position worth offering, or <see langword="null"/>.</returns>
    public async Task<TimeSpan?> BeginAsync(string mediaId, TimeSpan? duration)
    {
        ArgumentException.ThrowIfNullOrEmpty(mediaId);
        _mediaId = null;
        var text = await _settings.GetAsync(SettingPrefix + mediaId, CancellationToken.None).ConfigureAwait(true);
        _mediaId = mediaId;
        _saved = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0
            ? TimeSpan.FromSeconds(seconds)
            : null;
        _savedAt = DateTimeOffset.MinValue;
        return _saved is { } position && IsWorthOffering(position, duration) ? position : null;
    }

    /// <summary>
    /// Checks whether a position is far enough from both ends to offer continuing.
    /// </summary>
    /// <param name="position">The position.</param>
    /// <param name="duration">The duration, when known.</param>
    /// <returns><see langword="true"/> when it is worth offering.</returns>
    public static bool IsWorthOffering(TimeSpan position, TimeSpan? duration)
        => position >= MinimumPosition && (duration is not { } total || position < total - EndMargin);

    /// <summary>
    /// Reports the current position; it is saved at most every <see cref="SaveInterval"/> during playback and at once
    /// when playback stops.
    /// </summary>
    /// <param name="mediaId">The media identifier.</param>
    /// <param name="position">The position.</param>
    /// <param name="duration">The duration, when known.</param>
    /// <param name="playing">Whether the film plays.</param>
    /// <param name="now">The current time.</param>
    public void Report(string mediaId, TimeSpan position, TimeSpan? duration, bool playing, DateTimeOffset now)
    {
        if (mediaId != _mediaId)
        {
            return;
        }

        var finished = duration is { } total && total > EndMargin && position >= total - EndMargin;
        var value = finished ? TimeSpan.Zero : position;
        if (!finished && value < MinimumPosition)
        {
            // The first minute is not remembered, but it does not erase an earlier stop either.
            return;
        }

        var changed = _saved is not { } saved || Math.Abs((saved - value).TotalSeconds) >= 1;
        var due = playing ? now - _savedAt >= SaveInterval : true;
        if (!changed || !due)
        {
            return;
        }

        _saved = value;
        _savedAt = now;
        _ = _settings.SetAsync(
            SettingPrefix + mediaId,
            Math.Round(value.TotalSeconds, 1).ToString(CultureInfo.InvariantCulture),
            CancellationToken.None);
    }
}
