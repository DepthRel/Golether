using System.Collections.Concurrent;
using Golether.Media.Player.Mpv;

namespace Golether.Media.Player.Tests;

/// <summary>
/// Tests of the real libmpv prepared in <c>Native/&lt;runtime&gt;</c>: mouse bindings of the video and local volume.
/// Skipped when the library is not there.
/// </summary>
public sealed class MpvInputTests
{
    /// <summary>
    /// A left click and a double click on the video are reported, other keys are not; volume and mute are accepted.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task MouseBindings_ReportClicks()
    {
        var library = FindLibrary();
        Assert.SkipWhen(library is null, "libmpv is not prepared (run golether-components fetch).");
        MpvLibraryResolver.PreferredPath = library;
        Assert.True(MpvPlayer.TryCreate(new MpvPlayerOptions(), null, out var player, out var error), error);
        await using var _ = player!;
        var actions = new ConcurrentQueue<VideoPointerAction>();
        player!.VideoPointer += (_, action) => actions.Enqueue(action);

        player.SimulateKey("MBTN_RIGHT");
        player.SimulateKey("SPACE");
        player.SimulateKey("MBTN_LEFT");
        player.SimulateKey("MBTN_LEFT_DBL");
        for (var i = 0; i < 100 && actions.Count < 2; i++)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        await Task.Delay(200, TestContext.Current.CancellationToken);
        Assert.Equal([VideoPointerAction.Click, VideoPointerAction.DoubleClick], actions);

        player.SetVolume(35);
        player.SetMuted(true);
        player.SetVolume(250);
    }

    /// <summary>
    /// Finds libmpv in the repository.
    /// </summary>
    /// <returns>The path or <see langword="null"/>.</returns>
    private static string? FindLibrary()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var runtime = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "win-arm64" : "win-x64";
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "Native", runtime, "libmpv-2.dll");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
