using System.Text;
using Golether.Core.Data.Enums;
using Golether.Media.Player.Mpv;

namespace Golether.Media.Player.Tests;

/// <summary>
/// Tests of <see cref="MediaTrack"/> and of reading tracks from mpv.
/// </summary>
public sealed class MediaTrackTests
{
    /// <summary>
    /// Two- and three-letter codes, including bibliographic ones, name the same language.
    /// </summary>
    /// <param name="code">The code in the file.</param>
    /// <param name="normalized">The two-letter code.</param>
    [Theory]
    [InlineData("rus", "ru")]
    [InlineData("RU", "ru")]
    [InlineData("eng", "en")]
    [InlineData("ger", "de")]
    [InlineData("deu", "de")]
    [InlineData("fre", "fr")]
    public void Languages_AreNormalized(string code, string normalized)
    {
        Assert.Equal(normalized, MediaTrack.Normalize(code));
        var track = new MediaTrack(1, MediaTrackKind.Audio, null, code, null, null, false, false, false);
        Assert.True(track.HasLanguage(normalized));
        Assert.NotNull(track.LanguageName);
    }

    /// <summary>
    /// The label shows the title, the language, the channel layout, the codec and the origin.
    /// </summary>
    [Fact]
    public void DisplayName_DescribesTheTrack()
    {
        var dubbing = new MediaTrack(2, MediaTrackKind.Audio, "Дубляж", "eng", "ac3", 6, false, false, true);
        var parts = dubbing.DisplayName.Split(" · ");
        Assert.Equal("Дубляж", parts[0]);
        Assert.Equal(["5.1", "AC3"], parts[2..]);

        var external = new MediaTrack(3, MediaTrackKind.Subtitle, null, null, "subrip", null, false, true, true);
        Assert.Equal("Дорожка 3 · SUBRIP · из файла", external.DisplayName);
        Assert.Null(MediaTrack.DescribeLanguage("und"));
    }

    /// <summary>
    /// Video tracks and broken entries are skipped; flags are read from mpv's yes/no text.
    /// </summary>
    [Fact]
    public void ReadTracks_ParsesTheTrackList()
    {
        var values = new Dictionary<string, string>
        {
            ["track-list/count"] = "4",
            ["track-list/0/type"] = "video",
            ["track-list/0/id"] = "1",
            ["track-list/1/type"] = "audio",
            ["track-list/1/id"] = "1",
            ["track-list/1/lang"] = "rus",
            ["track-list/1/codec"] = "aac",
            ["track-list/1/demux-channel-count"] = "2",
            ["track-list/1/selected"] = "yes",
            ["track-list/1/default"] = "yes",
            ["track-list/2/type"] = "sub",
            ["track-list/2/id"] = "1",
            ["track-list/2/title"] = "Forced",
            ["track-list/2/lang"] = "",
            ["track-list/2/selected"] = "no",
            ["track-list/3/type"] = "sub",
            ["track-list/3/id"] = "oops",
        };

        var tracks = MpvPlayer.ReadTracks(name => values.GetValueOrDefault(name));

        Assert.Equal(2, tracks.Count);
        Assert.Equal(new MediaTrack(1, MediaTrackKind.Audio, null, "rus", "aac", 2, true, false, true), tracks[0]);
        Assert.Equal(new MediaTrack(1, MediaTrackKind.Subtitle, "Forced", null, null, null, false, false, false), tracks[1]);
        Assert.Empty(MpvPlayer.ReadTracks(_ => null));
    }

    /// <summary>
    /// The seekable ranges are read from the JSON of <c>demuxer-cache-state</c>; broken values are ignored.
    /// </summary>
    [Fact]
    public void CacheRanges_AreParsed()
    {
        var ranges = MpvPlayer.ParseCacheRanges("""{"cache-end":42.5,"seekable-ranges":[{"start":0,"end":12.25},{"start":60,"end":90.5},{"start":5,"end":1},{"start":"x","end":2}],"bof-cached":true}""");
        Assert.Equal(
            [new Core.Playback.MediaTimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(12.25)), new Core.Playback.MediaTimeRange(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(90.5))],
            ranges);
        Assert.Empty(MpvPlayer.ParseCacheRanges(null));
        Assert.Empty(MpvPlayer.ParseCacheRanges("not json"));
        Assert.Empty(MpvPlayer.ParseCacheRanges("""{"seekable-ranges":7}"""));
    }

    /// <summary>
    /// With the real libmpv: a sound file shows its track, subtitles from a file are added and selected, and can be
    /// switched off.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task RealPlayer_ListsAndSelectsTracks()
    {
        var library = MpvInputTests.FindLibrary();
        Assert.SkipWhen(library is null, "libmpv is not prepared (run golether-components fetch).");
        MpvLibraryResolver.PreferredPath = library;
        var folder = Directory.CreateTempSubdirectory("golether-tracks-");
        try
        {
            var sound = Path.Combine(folder.FullName, "tone.wav");
            WriteSilence(sound, TimeSpan.FromSeconds(3));
            var subtitles = Path.Combine(folder.FullName, "tone.ru.srt");
            await File.WriteAllTextAsync(subtitles, "1\n00:00:00,000 --> 00:00:02,000\nПривет\n", Encoding.UTF8, TestContext.Current.CancellationToken);

            Assert.True(MpvPlayer.TryCreate(new MpvPlayerOptions(), null, out var player, out var error), error);
            await using var _ = player!;
            player!.SetVolume(0);
            var changes = 0;
            player.TracksChanged += (_, _) => Interlocked.Increment(ref changes);

            await player.LoadAsync(new Uri(sound), TimeSpan.Zero, paused: true, TestContext.Current.CancellationToken);
            await WaitFor(() => player.GetTracks().Any(t => t.Kind == MediaTrackKind.Audio));
            var audio = Assert.Single(player.GetTracks(), t => t.Kind == MediaTrackKind.Audio);
            await WaitFor(() => player.GetSnapshot().Buffered is { Count: > 0 });
            var cached = player.GetSnapshot().Buffered![0];
            Assert.Equal(TimeSpan.Zero, cached.Start);
            Assert.InRange(cached.End.TotalSeconds, 0.5, 3.5);
            Assert.True(audio.IsSelected);

            player.AddSubtitleFile(subtitles);
            await WaitFor(() => player.GetTracks().Any(t => t is { Kind: MediaTrackKind.Subtitle, IsSelected: true }));
            var subtitle = player.GetTracks().Last(t => t.Kind == MediaTrackKind.Subtitle);
            Assert.True(subtitle.IsExternal);

            player.SelectTrack(MediaTrackKind.Subtitle, null);
            await WaitFor(() => !player.GetTracks().Any(t => t is { Kind: MediaTrackKind.Subtitle, IsSelected: true }));
            Assert.True(changes > 0);
            Assert.Throws<FileNotFoundException>(() => player.AddSubtitleFile(Path.Combine(folder.FullName, "missing.srt")));
        }
        finally
        {
            try
            {
                folder.Delete(recursive: true);
            }
            catch (IOException)
            {
                // mpv may still hold the file for a moment.
            }
        }
    }

    /// <summary>
    /// With the real libmpv: switching between two sound tracks and between two subtitle tracks really changes what
    /// the player uses, and the change is reported back.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task RealPlayer_SwitchesBetweenTracks()
    {
        var library = MpvInputTests.FindLibrary();
        Assert.SkipWhen(library is null, "libmpv is not prepared (run golether-components fetch).");
        MpvLibraryResolver.PreferredPath = library;
        var folder = Directory.CreateTempSubdirectory("golether-switch-");
        try
        {
            var first = Path.Combine(folder.FullName, "original.wav");
            var second = Path.Combine(folder.FullName, "dubbing.wav");
            WriteSilence(first, TimeSpan.FromSeconds(5));
            WriteSilence(second, TimeSpan.FromSeconds(5));
            var russian = Path.Combine(folder.FullName, "film.ru.srt");
            var english = Path.Combine(folder.FullName, "film.en.srt");
            await File.WriteAllTextAsync(russian, "1\n00:00:00,000 --> 00:00:02,000\nПривет\n", Encoding.UTF8, TestContext.Current.CancellationToken);
            await File.WriteAllTextAsync(english, "1\n00:00:00,000 --> 00:00:02,000\nHello\n", Encoding.UTF8, TestContext.Current.CancellationToken);

            Assert.True(MpvPlayer.TryCreate(new MpvPlayerOptions(), null, out var player, out var error), error);
            await using var _ = player!;
            player!.SetVolume(0);
            await player.LoadAsync(new Uri(first), TimeSpan.Zero, paused: true, TestContext.Current.CancellationToken);
            await WaitFor(() => player.GetTracks().Any(t => t.Kind == MediaTrackKind.Audio));

            player.AddAudioFile(second);
            player.AddSubtitleFile(russian);
            player.AddSubtitleFile(english);
            await WaitFor(() => player.GetTracks().Count(t => t.Kind == MediaTrackKind.Audio) == 2
                && player.GetTracks().Count(t => t.Kind == MediaTrackKind.Subtitle) == 2);

            // The sound track really switches to the other one and back.
            var audio = player.GetTracks().Where(t => t.Kind == MediaTrackKind.Audio).ToArray();
            player.SelectTrack(MediaTrackKind.Audio, audio[0].Id);
            await WaitFor(() => Selected(player, MediaTrackKind.Audio) == audio[0].Id);
            player.SelectTrack(MediaTrackKind.Audio, audio[1].Id);
            await WaitFor(() => Selected(player, MediaTrackKind.Audio) == audio[1].Id);

            // The same for subtitles, including switching them off.
            var subtitles = player.GetTracks().Where(t => t.Kind == MediaTrackKind.Subtitle).ToArray();
            player.SelectTrack(MediaTrackKind.Subtitle, subtitles[0].Id);
            await WaitFor(() => Selected(player, MediaTrackKind.Subtitle) == subtitles[0].Id);
            player.SelectTrack(MediaTrackKind.Subtitle, subtitles[1].Id);
            await WaitFor(() => Selected(player, MediaTrackKind.Subtitle) == subtitles[1].Id);
            player.SelectTrack(MediaTrackKind.Subtitle, null);
            await WaitFor(() => Selected(player, MediaTrackKind.Subtitle) is null);
        }
        finally
        {
            try
            {
                folder.Delete(recursive: true);
            }
            catch (IOException)
            {
                // mpv may still hold the files for a moment.
            }
        }
    }

    /// <summary>
    /// Returns the selected track of a kind.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="kind">The kind.</param>
    /// <returns>The identifier, or <see langword="null"/> when nothing is selected.</returns>
    private static long? Selected(MpvPlayer player, MediaTrackKind kind)
        => player.GetTracks().FirstOrDefault(t => t.Kind == kind && t.IsSelected)?.Id;

    /// <summary>
    /// Waits until a condition holds.
    /// </summary>
    /// <param name="condition">The condition.</param>
    /// <returns>A task that completes when the condition holds.</returns>
    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The player did not reach the expected state.");
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    /// Writes a silent 16-bit mono PCM file.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="duration">The duration.</param>
    private static void WriteSilence(string path, TimeSpan duration)
    {
        const int rate = 8000;
        var samples = (int)(rate * duration.TotalSeconds);
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8);
        writer.Write(36 + (samples * 2));
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(rate);
        writer.Write(rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(samples * 2);
        writer.Write(new byte[samples * 2]);
    }
}
