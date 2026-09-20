namespace Golether.Media.Player.Mpv;

/// <summary>
/// Value formats of the libmpv client API (<c>mpv_format</c>).
/// </summary>
internal enum MpvFormat
{
    /// <summary>
    /// No data.
    /// </summary>
    None = 0,

    /// <summary>
    /// A zero-terminated UTF-8 string.
    /// </summary>
    String = 1,

    /// <summary>
    /// A boolean stored as <c>int</c>.
    /// </summary>
    Flag = 3,

    /// <summary>
    /// A signed 64-bit integer.
    /// </summary>
    Int64 = 4,

    /// <summary>
    /// A double.
    /// </summary>
    Double = 5,
}
