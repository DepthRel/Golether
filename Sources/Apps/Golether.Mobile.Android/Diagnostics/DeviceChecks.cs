using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using Android.Content;
using Android.Util;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Golether.Client.Services;
using Golether.Core.Configuration;
using Golether.Core.Data;
using Golether.Core.Data.Enums;
using Golether.Core.Data.Migrations.SQLite.Runner;
using Golether.Core.Data.Stores;
using Golether.Core.Identity;
using Golether.Core.Networking;
using Golether.Media.Conference.GStreamer;
using Golether.Media.Conference.GStreamer.Android;
using Golether.Media.Conference.GStreamer.Native;
using Golether.Media.Player.Android;
using Golether.Media.Player.Mpv;
using Golether.Mobile.Controls;
using Golether.Security.Android;
using Golether.Security.Identity;
using Golether.Transports.Tls;

namespace Golether.Mobile.Android.Diagnostics;

/// <summary>
/// Checks of what can be proved only on a device. Built with <c>-p:GoletherDeviceChecks=true</c> and started by the
/// <c>golether.checks</c> extra of the activity; every result is a line of logcat with the tag <see cref="TAG"/>.
/// </summary>
internal static class DeviceChecks
{
    /// <summary>
    /// The logcat tag of the results.
    /// </summary>
    private const string TAG = "GOLETHER-CHECK";

    /// <summary>
    /// The setting written and read back by the data check.
    /// </summary>
    private const string SETTING = "checks.value";

    /// <summary>
    /// The test host: the computer as the emulator sees it, on the default port of a session.
    /// </summary>
    private static readonly PeerEndpoint _testHost = new("10.0.2.2", TlsTransportOptions.DEFAULT_PORT);

    /// <summary>
    /// Runs the checks in the background.
    /// </summary>
    /// <param name="context">The application context.</param>
    /// <param name="intent">The intent of the activity: <c>golether.tls.peer</c> names the key of a test host.</param>
    public static void Start(Context context, Intent intent)
    {
        var tlsPeer = intent.GetStringExtra("golether.tls.peer");
        var video = intent.GetStringExtra("golether.video");
        var throughStream = intent.GetBooleanExtra("golether.video.stream", false);
        var conference = intent.GetBooleanExtra("golether.conference", false);
        var realDevices = intent.GetBooleanExtra("golether.conference.real", false);

        _ = Task.Run(async () =>
        {
            var paths = AppDataPaths.Resolve(getEnvironmentVariable: name => name == AppDataPaths.DATA_DIRECTORY_VARIABLE ? context.FilesDir!.AbsolutePath : null);
            await RunAsync("data", () => CheckDataAsync(paths));
            await RunAsync("secrets", CheckSecretsAsync);
            await RunAsync("identity", () => CheckIdentityAsync(paths));
            await RunAsync("identity-recovered", () => CheckRecoveryAsync(paths));
            await RunAsync("mpv", CheckPlayerAsync);

            if (tlsPeer is not null)
            {
                await RunAsync("tls", () => CheckTlsAsync(paths, PeerId.Parse(tlsPeer)));
                await RunAsync("tls-pin-mismatch", () => CheckPinMismatchAsync(paths));
            }

            if (conference)
            {
                await RunAsync("gst", () => CheckGStreamerAsync(context));
                await RunAsync("call", () => CheckCallAsync(paths, useTestSources: !realDevices));
            }

            if (video is not null)
            {
                var path = Path.Combine(context.GetExternalFilesDir(null)!.AbsolutePath, video);
                await RunAsync("video", () => CheckVideoAsync(path, throughStream));
            }

            Log.Info(TAG, "done");
        });
    }

    /// <summary>
    /// Runs one check and writes its result.
    /// </summary>
    /// <param name="name">The name of the check.</param>
    /// <param name="check">The check; returns details of a success.</param>
    /// <returns>A task that completes when the result is written.</returns>
    private static async Task RunAsync(string name, Func<Task<string>> check)
    {
        try
        {
            Log.Info(TAG, $"{name} OK {await check()}");
        }
        catch (Exception ex)
        {
            Log.Error(TAG, $"{name} FAIL {ex}");
        }
    }

    /// <summary>
    /// Migrates the database in the data folder of the application and reads a setting back through EF Core.
    /// </summary>
    /// <param name="paths">The data paths.</param>
    /// <returns>The migrations and the value.</returns>
    private static async Task<string> CheckDataAsync(AppDataPaths paths)
    {
        paths.EnsureCreated();
        var applied = SqliteDatabaseMigrator.MigrateUp(paths.ConnectionString);

        await using var services = new ServiceCollection().AddLogging().AddSingleton(TimeProvider.System).AddGoletherData(paths.ConnectionString).BuildServiceProvider();
        var settings = services.GetRequiredService<ISettingsStore>();
        var value = Guid.NewGuid().ToString("N");
        await settings.SetAsync(SETTING, value, CancellationToken.None);
        var stored = await settings.GetAsync(SETTING, CancellationToken.None);

        return stored == value ? $"migrations={applied.Count}" : throw new InvalidOperationException($"Read back {stored}");
    }

    /// <summary>
    /// Protects and reads data, and makes sure changed data is refused.
    /// </summary>
    /// <returns>The details.</returns>
    private static Task<string> CheckSecretsAsync()
    {
        var protector = new AndroidKeystoreSecretProtector();
        var plain = Encoding.UTF8.GetBytes("golether");

        var protectedData = protector.Protect(plain);
        var read = protector.Unprotect(protectedData);
        if (!read.SequenceEqual(plain))
        {
            throw new InvalidOperationException("The data did not come back.");
        }

        protectedData[^1] ^= 1;
        try
        {
            protector.Unprotect(protectedData);
        }
        catch (CryptographicException)
        {
            return Task.FromResult($"size={protectedData.Length} tamper-refused");
        }

        throw new InvalidOperationException("Changed data was accepted.");
    }

    /// <summary>
    /// Makes sure the device key survives a second load.
    /// </summary>
    /// <param name="paths">The data paths.</param>
    /// <returns>The short identifier of the device.</returns>
    private static Task<string> CheckIdentityAsync(AppDataPaths paths)
    {
        var first = CreateStore(paths).LoadOrCreate();
        var second = CreateStore(paths).LoadOrCreate();

        return first.PeerId == second.PeerId
            ? Task.FromResult(first.PeerId.ToShortString())
            : throw new InvalidOperationException("The device key changed between loads.");
    }

    /// <summary>
    /// Damages the stored key and makes sure a new one takes its place without a failure.
    /// </summary>
    /// <param name="paths">The data paths.</param>
    /// <returns>The short identifier of the new device key.</returns>
    private static Task<string> CheckRecoveryAsync(AppDataPaths paths)
    {
        var store = new FileDeviceIdentityStore(paths.IdentityDirectory, new AndroidKeystoreSecretProtector(), TimeProvider.System);
        File.WriteAllBytes(store.FilePath, [1, 2, 3]);

        return Task.FromResult(CreateStore(paths).LoadOrCreate().PeerId.ToShortString());
    }

    /// <summary>
    /// Loads libmpv from the package and creates a player.
    /// </summary>
    /// <returns>A note about the player.</returns>
    private static async Task<string> CheckPlayerAsync()
    {
        MediaCodecSupport.Initialize();

        if (!MpvPlayer.TryCreate(new MpvPlayerOptions { HardwareDecoding = "mediacodec-copy" }, null, out var player, out var error))
        {
            throw new InvalidOperationException(error);
        }

        await player!.DisposeAsync();

        return "created";
    }

    /// <summary>
    /// Plays a file through the render API with text of the interface over the picture, then changes the speed and
    /// seeks. The player stays on the screen for a screenshot.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="throughStream">Whether the file goes through the protocol of the session instead of a path.</param>
    /// <returns>The positions the player reported.</returns>
    private static async Task<string> CheckVideoAsync(string path, bool throughStream)
    {
        MediaCodecSupport.Initialize();
        var options = new MpvPlayerOptions { UseRenderApi = true, HardwareDecoding = "mediacodec-copy" };
        if (!MpvPlayer.TryCreate(options, null, out var player, out var error))
        {
            throw new InvalidOperationException(error);
        }

        MpvVideoView? view = null;
        Grid? page = null;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            view = new MpvVideoView { Player = player };
            page = new Grid { Background = Brushes.Black };
            page.Children.Add(view);
            page.Children.Add(new TextBlock
            {
                Text = "overlay",
                FontSize = 40,
                Foreground = Brushes.Yellow,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
            });
            var lifetime = (ISingleViewApplicationLifetime)Avalonia.Application.Current!.ApplicationLifetime!;
            ((UserControl)lifetime.MainView!).Content = page;
        });
        await Task.Delay(TimeSpan.FromSeconds(2));

        var source = throughStream ? new MpvMediaUriRegistry().Register(() => File.OpenRead(path)) : new Uri(path);
        await player!.LoadAsync(source, TimeSpan.Zero, paused: false);
        await Task.Delay(TimeSpan.FromSeconds(5));
        var playing = player.GetSnapshot();

        await player.SetRateAsync(1.05);
        await player.SeekAsync(TimeSpan.FromSeconds(12));
        await Task.Delay(TimeSpan.FromSeconds(2));
        var moved = player.GetSnapshot();

        // The view leaves the tree and comes back, as it does on a change of screens: the surface goes and returns.
        await Dispatcher.UIThread.InvokeAsync(() => page!.Children.Remove(view!));
        await Task.Delay(TimeSpan.FromSeconds(1));
        await Dispatcher.UIThread.InvokeAsync(() => page!.Children.Insert(0, view!));
        await Task.Delay(TimeSpan.FromSeconds(2));
        var returned = player.GetSnapshot();

        // The end of the file: mpv keeps the last frame and pauses there.
        await player.SeekAsync(TimeSpan.FromSeconds(19));
        await Task.Delay(TimeSpan.FromSeconds(3));

        return $"loaded={playing.IsLoaded} position={playing.Position} duration={playing.Duration} afterSeek={moved.Position} rate={moved.Rate} " +
               $"reattached: loaded={returned.IsLoaded} position={returned.Position}";
    }

    /// <summary>
    /// Starts GStreamer from the package.
    /// </summary>
    /// <param name="context">The application context.</param>
    /// <returns>The version.</returns>
    private static async Task<string> CheckGStreamerAsync(Context context)
    {
        await Dispatcher.UIThread.InvokeAsync(() => GStreamerAndroid.Initialize(context));

        return GstRuntime.TryInitialize(null, Path.Combine(context.CacheDir!.AbsolutePath, "gst-registry.bin"), out var error)
            ? GstRuntime.Version ?? "unknown"
            : throw new InvalidOperationException(error);
    }

    /// <summary>
    /// Connects two conference instances in this process, as the tests of the desktop do: frames, sound and the data
    /// channel must pass through webrtcbin both ways.
    /// </summary>
    /// <param name="paths">The data paths.</param>
    /// <param name="useTestSources">Whether test patterns replace the camera and the microphone of the first side.</param>
    /// <returns>What the connection carried.</returns>
    private static async Task<string> CheckCallAsync(AppDataPaths paths, bool useTestSources)
    {
        var alice = PeerId.Parse(new string('1', 64));
        var bob = PeerId.Parse(new string('2', 64));
        var registry = Path.Combine(paths.Root, "gst-registry.bin");
        await using var first = new GStreamerConferenceMedia(new GStreamerConferenceOptions { RegistryFile = registry, UseTestSources = useTestSources, EnablePlayback = false });
        await using var second = new GStreamerConferenceMedia(new GStreamerConferenceOptions { RegistryFile = registry, UseTestSources = true, EnablePlayback = false });

        var framesAtFirst = 0;
        var framesAtSecond = 0;
        first.VideoFrameReceived += (_, frame) =>
        {
            if (frame.PeerId == bob)
            {
                Interlocked.Increment(ref framesAtFirst);
            }
        };
        second.VideoFrameReceived += (_, frame) =>
        {
            if (frame.PeerId == alice)
            {
                Interlocked.Increment(ref framesAtSecond);
            }
        };
        first.SignalReady += (_, signal) => _ = second.HandleSignalAsync(signal with { PeerId = alice }, CancellationToken.None);
        second.SignalReady += (_, signal) => _ = first.HandleSignalAsync(signal with { PeerId = bob }, CancellationToken.None);

        await first.StartCaptureAsync(null, null, CancellationToken.None);
        await second.StartCaptureAsync(null, null, CancellationToken.None);
        await first.ConnectAsync(bob, isOfferer: true, CancellationToken.None);

        for (var i = 0; i < 300 && !(Volatile.Read(ref framesAtFirst) > 0 && Volatile.Read(ref framesAtSecond) > 0 && first.DataPeers.Contains(bob)); i++)
        {
            await Task.Delay(100);
        }

        var link = first.GetPeerDiagnostics().Single();
        var report = $"verified={link.Verified} sent={link.VideoSent} received={link.VideoReceived} audioSent={link.AudioSent} data={first.DataPeers.Contains(bob)}";

        return framesAtFirst > 0 && framesAtSecond > 0 ? report : throw new InvalidOperationException($"No frames: {report}");
    }

    /// <summary>
    /// Connects to the test host on the computer with mutual TLS and a pinned key, and exchanges a line with it.
    /// </summary>
    /// <param name="paths">The data paths.</param>
    /// <param name="hostPeer">The key of the host.</param>
    /// <returns>The TLS version and the key of this device.</returns>
    private static async Task<string> CheckTlsAsync(AppDataPaths paths, PeerId hostPeer)
    {
        var identity = CreateStore(paths).LoadOrCreate();
        var connector = new TlsPeerConnector(identity, new TlsTransportOptions());

        await using var stream = await connector.ConnectAsync(_testHost, hostPeer, StreamPurpose.Control, CancellationToken.None);
        var message = Encoding.UTF8.GetBytes("ping");
        await stream.Stream.WriteAsync(message);
        await stream.Stream.FlushAsync();

        var echo = new byte[message.Length];
        await stream.Stream.ReadExactlyAsync(echo);
        if (!echo.SequenceEqual(message))
        {
            throw new InvalidOperationException("The host did not echo the line.");
        }

        return $"{(stream.Stream as SslStream)?.SslProtocol} self={identity.PeerId}";
    }

    /// <summary>
    /// Makes sure a host with another key is refused.
    /// </summary>
    /// <param name="paths">The data paths.</param>
    /// <returns>The reason of the refusal.</returns>
    private static async Task<string> CheckPinMismatchAsync(AppDataPaths paths)
    {
        var connector = new TlsPeerConnector(CreateStore(paths).LoadOrCreate(), new TlsTransportOptions());

        try
        {
            await using var stream = await connector.ConnectAsync(_testHost, PeerId.Parse(new string('a', 64)), StreamPurpose.Control, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or AuthenticationException)
        {
            return ex.GetType().Name;
        }

        throw new InvalidOperationException("A host with another key was accepted.");
    }

    /// <summary>
    /// Creates the key store of the mobile client.
    /// </summary>
    /// <param name="paths">The data paths.</param>
    /// <returns>The store.</returns>
    private static RecoveringDeviceIdentityStore CreateStore(AppDataPaths paths)
        => new(new FileDeviceIdentityStore(paths.IdentityDirectory, new AndroidKeystoreSecretProtector(), TimeProvider.System));
}
