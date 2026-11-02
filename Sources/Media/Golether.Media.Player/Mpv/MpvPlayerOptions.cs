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

    /// <summary>
    /// Gets a value indicating whether the application draws the video itself through
    /// <see cref="MpvPlayer.CreateRenderContext"/>; <see cref="WindowHandle"/> is not used then.
    /// </summary>
    public bool UseRenderApi { get; init; }

    /// <summary>
    /// Gets a value indicating whether the subtitles of a file may start by themselves (default <see langword="true"/>).
    /// </summary>
    public bool SubtitlesAtStart { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether the film is drawn with the cheapest filters: on a phone the default ones keep
    /// the graphics busy for every frame, warm the device and empty the battery, and on its small screen nobody sees
    /// the difference.
    /// </summary>
    public bool LowPower { get; init; }
}
