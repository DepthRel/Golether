using Golether.Core.Data.Enums;

namespace Golether.Media.Player;

/// <summary>
/// Settings and input of a player that concern only this device: they are never synchronized.
/// </summary>
public interface ILocalPlayerControls
{
    /// <summary>
    /// Raised when the user clicks the video picture. May be raised on any thread.
    /// </summary>
    event EventHandler<VideoPointerAction>? VideoPointer;

    /// <summary>
    /// Sets the playback volume of this device.
    /// </summary>
    /// <param name="percent">The volume, 0–100.</param>
    void SetVolume(double percent);

    /// <summary>
    /// Mutes or unmutes the playback on this device.
    /// </summary>
    /// <param name="muted">Whether the sound is off.</param>
    void SetMuted(bool muted);

    /// <summary>
    /// Raised when the tracks of the loaded file or the selected tracks change. May be raised on any thread.
    /// </summary>
    event EventHandler? TracksChanged;

    /// <summary>
    /// Returns the sound and subtitle tracks of the loaded file.
    /// </summary>
    /// <returns>The tracks, empty when nothing is loaded.</returns>
    IReadOnlyList<MediaTrack> GetTracks();

    /// <summary>
    /// Selects a track on this device only.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <param name="id">The track, or <see langword="null"/> to switch the kind off (subtitles).</param>
    void SelectTrack(MediaTrackKind kind, long? id);

    /// <summary>
    /// Loads subtitles from a file on this device and shows them.
    /// </summary>
    /// <param name="path">The subtitle file (.srt, .ass, .vtt …).</param>
    void AddSubtitleFile(string path);

    /// <summary>
    /// Loads a sound track from a file on this device and plays it (an external dubbing).
    /// </summary>
    /// <param name="path">The sound file.</param>
    void AddAudioFile(string path);

    /// <summary>
    /// Returns the width-to-height ratio of the picture as it is shown. The strokes drawn over the video are placed
    /// by it, so everybody sees them over the same part of the picture whatever the size of their window.
    /// </summary>
    /// <returns>The ratio, or <c>0</c> when it is not known yet.</returns>
    double GetVideoAspect();
}
