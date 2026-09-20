namespace Golether.Media.Player.Mpv;

/// <summary>
/// Settings of <see cref="MpvPlayer"/>.
/// </summary>
public sealed record MpvPlayerOptions
{
    /// <summary>
    /// Gets the native window handle to render into (HWND on Windows, X11 window id on Linux); 0 opens mpv's own window.
    /// </summary>
    public nint WindowHandle { get; init; }

    /// <summary>
    /// Gets the directory with the user's <c>mpv.conf</c>, or <see langword="null"/> to ignore user configuration.
    /// </summary>
    public string? ConfigDirectory { get; init; }

    /// <summary>
    /// Gets the hardware decoding mode (default <c>auto-safe</c>).
    /// </summary>
    public string HardwareDecoding { get; init; } = "auto-safe";

    /// <summary>
    /// Gets the demuxer cache size (default <c>512MiB</c>).
    /// </summary>
    public string DemuxerCacheSize { get; init; } = "512MiB";
}
