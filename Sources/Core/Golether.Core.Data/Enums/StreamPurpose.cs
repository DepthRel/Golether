namespace Golether.Core.Data.Enums;

/// <summary>
/// The purpose of a peer stream, sent in the stream preamble.
/// </summary>
public enum StreamPurpose : byte
{
    /// <summary>
    /// Session control: hello, playback state, clock synchronization, statuses.
    /// </summary>
    Control = 1,

    /// <summary>
    /// Media data: chunk requests and responses. Several data streams run in parallel.
    /// </summary>
    MediaData = 2,
}
