using System.Globalization;
using Golether.Core.Playback;

namespace Golether.Session;

/// <summary>
/// Formats event feed texts.
/// </summary>
public static class SessionTexts
{
    /// <summary>
    /// Formats a media position as <c>h:mm:ss</c>.
    /// </summary>
    /// <param name="position">The position.</param>
    /// <returns>The text.</returns>
    public static string FormatPosition(TimeSpan position)
        => ((int)position.TotalHours).ToString(CultureInfo.InvariantCulture) + position.ToString(@"\:mm\:ss", CultureInfo.InvariantCulture);

    /// <summary>
    /// Describes a playback change.
    /// </summary>
    /// <param name="state">The new state.</param>
    /// <param name="name">The name of the originator.</param>
    /// <returns>The text.</returns>
    public static string Describe(PlaybackState state, string name)
    {
        ArgumentNullException.ThrowIfNull(state);
        var position = FormatPosition(state.Position);
        return state.Cause switch
        {
            PlaybackCause.Play => $"{name} запускает воспроизведение с {position}",
            PlaybackCause.Pause => $"{name} ставит на паузу на {position}",
            PlaybackCause.Seek => $"{name} перематывает на {position}",
            PlaybackCause.WaitingForParticipants => $"Пауза на {position}: ждём, пока все участники будут готовы",
            PlaybackCause.StartedWithoutWaiting => "Старт без ожидания: отставшие догонят",
            PlaybackCause.ParticipantsReady => "Все участники готовы, продолжаем",
            PlaybackCause.Ended => "Фильм закончился: «Смотреть сначала» запустит его с начала у всех",
            _ => "Файл готов к просмотру",
        };
    }
}
