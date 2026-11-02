using Golether.Media.Player.Mpv;

namespace Golether.Media.Player.Tests;

/// <summary>
/// Tests of <see cref="MpvOptionList"/>: the options mpv gets before initialization are pinned in their order.
/// </summary>
public sealed class MpvOptionListTests
{
    /// <summary>
    /// The options every player gets, in their order.
    /// </summary>
    private static readonly (string Name, string Value)[] _defaults =
    [
        ("terminal", "no"),
        ("idle", "yes"),
        ("keep-open", "yes"),
        ("input-default-bindings", "no"),
        ("input-vo-keyboard", "no"),
        ("osc", "no"),
        ("osd-level", "0"),
        ("hwdec", "auto-safe"),
        ("cache", "yes"),
        ("demuxer-max-bytes", "512MiB"),
        ("demuxer-readahead-secs", "30"),
        ("audio-pitch-correction", "yes"),
        ("hr-seek", "yes"),
        ("ytdl", "no"),
    ];

    /// <summary>
    /// Without a window and a configuration folder mpv gets the defaults and ignores user configuration.
    /// </summary>
    [Fact]
    public void Build_WithoutWindow_GivesDefaults()
    {
        var options = MpvOptionList.Build(new MpvPlayerOptions(), configDirectoryExists: false);

        Assert.Equal([.. _defaults, ("config", "no")], options);
    }

    /// <summary>
    /// The hardware decoding mode and the cache size come from the options.
    /// </summary>
    [Fact]
    public void Build_TakesDecodingAndCacheFromOptions()
    {
        var options = MpvOptionList.Build(new MpvPlayerOptions { HardwareDecoding = "no", DemuxerCacheSize = "64MiB" }, configDirectoryExists: false);

        Assert.Contains(("hwdec", "no"), options);
        Assert.Contains(("demuxer-max-bytes", "64MiB"), options);
    }

    /// <summary>
    /// A window handle makes mpv draw into it, right after the defaults.
    /// </summary>
    [Fact]
    public void Build_WithWindow_AddsWidAndForceWindow()
    {
        var options = MpvOptionList.Build(new MpvPlayerOptions { WindowHandle = 42 }, configDirectoryExists: false);

        Assert.Equal([.. _defaults, ("wid", "42"), ("force-window", "yes"), ("config", "no")], options);
    }

    /// <summary>
    /// With the render API the application draws the picture itself: mpv gets its own video output and no window, and
    /// a render does not wait for the time of the frame, because it runs on the thread of the interface.
    /// </summary>
    [Fact]
    public void Build_WithRenderApi_UsesLibmpvOutput()
    {
        var options = MpvOptionList.Build(new MpvPlayerOptions { UseRenderApi = true, WindowHandle = 42 }, configDirectoryExists: false);

        Assert.Equal([.. _defaults, ("vo", "libmpv"), ("video-timing-offset", "0"), ("fbo-format", "rgba8"), ("config", "no")], options);
    }

    /// <summary>
    /// On a battery the film is drawn with the cheapest filters: the default ones keep the graphics of a phone busy for
    /// every frame.
    /// </summary>
    [Fact]
    public void Build_WithLowPower_UsesTheCheapFilters()
    {
        var options = MpvOptionList.Build(new MpvPlayerOptions { LowPower = true }, configDirectoryExists: false);

        Assert.Equal(
            [
                .. _defaults,
                ("scale", "bilinear"),
                ("dscale", "bilinear"),
                ("cscale", "bilinear"),
                ("dither-depth", "no"),
                ("correct-downscaling", "no"),
                ("linear-downscaling", "no"),
                ("sigmoid-upscaling", "no"),
                ("deband", "no"),
                ("hdr-compute-peak", "no"),
                ("config", "no"),
            ],
            options);
    }

    /// <summary>
    /// Subtitles that must not appear are off from the start, so they do not flash before the first track update.
    /// </summary>
    [Fact]
    public void Build_WithoutSubtitlesAtStart_TurnsThemOff()
    {
        var options = MpvOptionList.Build(new MpvPlayerOptions { SubtitlesAtStart = false }, configDirectoryExists: false);

        Assert.Equal([.. _defaults, ("sid", "no"), ("config", "no")], options);
    }

    /// <summary>
    /// An existing configuration folder is used.
    /// </summary>
    [Fact]
    public void Build_WithConfigDirectory_UsesIt()
    {
        var options = MpvOptionList.Build(new MpvPlayerOptions { ConfigDirectory = "C:/mpv" }, configDirectoryExists: true);

        Assert.Equal([.. _defaults, ("config-dir", "C:/mpv"), ("config", "yes")], options);
    }

    /// <summary>
    /// A configuration folder that does not exist is ignored.
    /// </summary>
    [Fact]
    public void Build_WithMissingConfigDirectory_IgnoresIt()
    {
        var options = MpvOptionList.Build(new MpvPlayerOptions { ConfigDirectory = "C:/mpv" }, configDirectoryExists: false);

        Assert.Equal([.. _defaults, ("config", "no")], options);
    }
}
