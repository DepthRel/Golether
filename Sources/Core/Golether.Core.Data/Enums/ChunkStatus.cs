namespace Golether.Core.Data.Enums;

/// <summary>
/// The status of a chunk response.
/// </summary>
public enum ChunkStatus : byte
{
    /// <summary>
    /// The chunk follows.
    /// </summary>
    Ok = 0,

    /// <summary>
    /// The index is outside the file.
    /// </summary>
    OutOfRange = 1,

    /// <summary>
    /// The host shares no media or another file than requested.
    /// </summary>
    WrongMedia = 2,

    /// <summary>
    /// The host failed to read the file.
    /// </summary>
    ReadError = 3,
}
