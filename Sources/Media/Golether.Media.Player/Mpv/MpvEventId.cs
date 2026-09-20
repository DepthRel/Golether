namespace Golether.Media.Player.Mpv;

/// <summary>
/// Event identifiers of the libmpv client API (<c>mpv_event_id</c>).
/// </summary>
internal enum MpvEventId
{
    /// <summary>
    /// No event (timeout or wakeup).
    /// </summary>
    None = 0,

    /// <summary>
    /// The player is shutting down.
    /// </summary>
    Shutdown = 1,

    /// <summary>
    /// A log message.
    /// </summary>
    LogMessage = 2,

    /// <summary>
    /// A file starts loading.
    /// </summary>
    StartFile = 6,

    /// <summary>
    /// A message sent with <c>script-message</c>, for example by an input binding.
    /// </summary>
    ClientMessage = 16,

    /// <summary>
    /// A file stopped playing.
    /// </summary>
    EndFile = 7,

    /// <summary>
    /// A file was loaded.
    /// </summary>
    FileLoaded = 8,

    /// <summary>
    /// Playback restarted after a seek or load.
    /// </summary>
    PlaybackRestart = 21,

    /// <summary>
    /// An observed property changed.
    /// </summary>
    PropertyChange = 22,
}
