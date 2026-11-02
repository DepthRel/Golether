using System.Globalization;

namespace Golether.Media.Player.Mpv;

/// <summary>
/// The options mpv gets before initialization, built from <see cref="MpvPlayerOptions"/>.
/// </summary>
internal static class MpvOptionList
{
    /// <summary>
    /// Builds the options in the order they are applied.
    /// </summary>
    /// <param name="options">The player settings.</param>
    /// <param name="configDirectoryExists">Whether <see cref="MpvPlayerOptions.ConfigDirectory"/> exists on disk.</param>
    /// <returns>The option names and values.</returns>
    public static IReadOnlyList<(string Name, string Value)> Build(MpvPlayerOptions options, bool configDirectoryExists)
    {
        var settings = new List<(string Name, string Value)>
        {
            ("terminal", "no"),
            ("idle", "yes"),
            ("keep-open", "yes"),
            ("input-default-bindings", "no"),
            ("input-vo-keyboard", "no"),
            ("osc", "no"),
            ("osd-level", "0"),
            ("hwdec", options.HardwareDecoding),
            ("cache", "yes"),
            ("demuxer-max-bytes", options.DemuxerCacheSize),
            ("demuxer-readahead-secs", "30"),
            ("audio-pitch-correction", "yes"),
            ("hr-seek", "yes"),
            ("ytdl", "no"),
        };

        if (options.UseRenderApi)
        {
            settings.Add(("vo", "libmpv"));
            settings.Add(("video-timing-offset", "0"));

            // A still frame reaches the target of the application by a blit, and OpenGL ES has no blit from a floating
            // point format to a normal one: the frame at the end of a file would stay black.
            settings.Add(("fbo-format", "rgba8"));
        }
        else if (options.WindowHandle != 0)
        {
            settings.Add(("wid", options.WindowHandle.ToString(CultureInfo.InvariantCulture)));
            settings.Add(("force-window", "yes"));
        }

        if (options.LowPower)
        {
            // What the built-in profile "fast" of mpv sets, spelled out so it does not depend on the version.
            settings.Add(("scale", "bilinear"));
            settings.Add(("dscale", "bilinear"));
            settings.Add(("cscale", "bilinear"));
            settings.Add(("dither-depth", "no"));
            settings.Add(("correct-downscaling", "no"));
            settings.Add(("linear-downscaling", "no"));
            settings.Add(("sigmoid-upscaling", "no"));
            settings.Add(("deband", "no"));
            settings.Add(("hdr-compute-peak", "no"));
        }

        if (!options.SubtitlesAtStart)
        {
            settings.Add(("sid", "no"));
        }

        if (options.ConfigDirectory is { } configDirectory && configDirectoryExists)
        {
            settings.Add(("config-dir", configDirectory));
            settings.Add(("config", "yes"));
        }
        else
        {
            settings.Add(("config", "no"));
        }

        return settings;
    }
}
