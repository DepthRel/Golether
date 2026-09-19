using System.ComponentModel;
using Golether.Tunnels.AmneziaWG.Configuration;
using Golether.Tunnels.AmneziaWG.Control;
using NSubstitute;

namespace Golether.Tunnels.AmneziaWG.Tests;

/// <summary>
/// Tests of the elevated tunnel helper and of the controller that starts it.
/// </summary>
public sealed class TunnelHelperTests : IDisposable
{
    /// <summary>
    /// The helper executable used in the tests.
    /// </summary>
    private const string Helper = @"C:\Golether\app\Golether.exe";

    /// <summary>
    /// The tunnels directory.
    /// </summary>
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("golether-helper-");

    /// <summary>
    /// The AmneziaWG executable used in the tests. It has to exist on disk: without the engine the controller
    /// refuses to start anything, helper or not.
    /// </summary>
    private readonly string _amneziaWg;

    /// <summary>
    /// Initializes a new instance of the <see cref="TunnelHelperTests"/> class.
    /// </summary>
    public TunnelHelperTests()
    {
        _amneziaWg = Path.Combine(_directory.FullName, "amneziawg.exe");
        File.WriteAllText(_amneziaWg, "engine");
    }

    /// <inheritdoc />
    public void Dispose() => _directory.Delete(recursive: true);

    /// <summary>
    /// The helper accepts only a configuration with a valid interface name and a result file next to it.
    /// </summary>
    [Fact]
    public void Arguments_AreValidated()
    {
        var config = WriteConfig("golether0");
        var result = Path.Combine(_directory.FullName, "x.result");

        Assert.True(TunnelHelper.TryParse(TunnelHelper.BuildArguments("up", config, result), out var verb, out var path, out _));
        Assert.Equal("up", verb);
        Assert.Equal(config, path);

        string[][] rejected =
        [
            ["--tunnel-helper", "up", config],
            ["--tunnel-helper", "delete", config, result],
            ["--tunnel-helper", "up", "golether0.conf", result],
            ["--tunnel-helper", "up", config, Path.Combine(Path.GetTempPath(), "elsewhere.result")],
            ["--tunnel-helper", "up", config, Path.Combine(_directory.FullName, "x.txt")],
            ["--tunnel-helper", "up", Path.Combine(_directory.FullName, "missing.conf"), result],
            ["--tunnel-helper", "up", WriteConfig("bad name!"), result],
            ["--tunnel-helper", "up", Path.ChangeExtension(config, ".exe"), result],
        ];
        foreach (var args in rejected)
        {
            Assert.False(TunnelHelper.TryParse(args, out _, out _, out _), string.Join(' ', args));
            Assert.Equal(TunnelHelper.InvalidArguments, TunnelHelper.Run(args, Substitute.For<IProcessRunner>(), _amneziaWg));
        }
    }

    /// <summary>
    /// "up" replaces the tunnel service; a failure is written to the result file.
    /// </summary>
    [Fact]
    public void Up_ReplacesServiceAndReportsFailure()
    {
        var config = WriteConfig("golether0");
        var result = Path.Combine(_directory.FullName, "r.result");
        var runner = Substitute.For<IProcessRunner>();
        runner.RunAsync(_amneziaWg, Arg.Is<IReadOnlyList<string>>(a => a[0] == "/uninstalltunnelservice"), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(1, string.Empty, "not installed"));
        runner.RunAsync(_amneziaWg, Arg.Is<IReadOnlyList<string>>(a => a[0] == "/installtunnelservice"), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, string.Empty, string.Empty), new ProcessResult(5, string.Empty, "Access is denied"));

        Assert.Equal(TunnelHelper.Success, TunnelHelper.Run(TunnelHelper.BuildArguments("up", config, result), runner, _amneziaWg));
        Assert.False(File.Exists(result));
        runner.Received(1).RunAsync(_amneziaWg, Arg.Is<IReadOnlyList<string>>(a => a.SequenceEqual(new[] { "/installtunnelservice", config })), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());

        Assert.Equal(TunnelHelper.Failure, TunnelHelper.Run(TunnelHelper.BuildArguments("up", config, result), runner, _amneziaWg));
        Assert.Contains("Access is denied", File.ReadAllText(result), StringComparison.Ordinal);
    }

    /// <summary>
    /// Without administrator rights the controller starts the helper through the elevated runner and passes on its
    /// error; a declined prompt gets a clear message.
    /// </summary>
    [Fact]
    public async Task Controller_UsesHelperWhenNotElevated()
    {
        var token = TestContext.Current.CancellationToken;
        var direct = Substitute.For<IProcessRunner>();
        var elevated = Substitute.For<IProcessRunner>();
        IReadOnlyList<string>? helperArgs = null;
        elevated.RunAsync(Helper, Arg.Any<IReadOnlyList<string>>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                helperArgs = call.ArgAt<IReadOnlyList<string>>(1);
                return new ProcessResult(0, string.Empty, string.Empty);
            });
        var options = new AwgCliOptions { ConfigDirectory = _directory.FullName, WindowsExecutable = _amneziaWg, HelperExecutable = Helper };
        var controller = new AwgCliTunnelController(direct, options, isWindows: true, elevatedRunner: elevated);

        await controller.UpAsync("golether0", HostTunnelInterface.Create().BuildConfiguration([]), token);

        Assert.NotNull(helperArgs);
        Assert.True(TunnelHelper.TryParse(helperArgs, out var verb, out var config, out _));
        Assert.Equal("up", verb);
        Assert.Equal(controller.GetConfigPath("golether0"), config);
        await direct.DidNotReceiveWithAnyArgs().RunAsync(default!, default!, default, Arg.Any<CancellationToken>());

        elevated.RunAsync(Helper, Arg.Any<IReadOnlyList<string>>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                File.WriteAllText(call.ArgAt<IReadOnlyList<string>>(1)[3], "amneziawg.exe завершился с кодом 5");
                return new ProcessResult(TunnelHelper.Failure, string.Empty, string.Empty);
            });
        var failure = await Assert.ThrowsAsync<TunnelControlException>(() => controller.DownAsync("golether0", token));
        Assert.Contains("кодом 5", failure.Message, StringComparison.Ordinal);
        Assert.Empty(_directory.GetFiles("*.result"));

        elevated.RunAsync(Helper, Arg.Any<IReadOnlyList<string>>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns<ProcessResult>(_ => throw new Win32Exception(ElevatedProcessRunner.Cancelled));
        var declined = await Assert.ThrowsAsync<TunnelControlException>(() => controller.DownAsync("golether0", token));
        Assert.Contains("права администратора", declined.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// On macOS awg-quick runs through the administrator prompt with every argument quoted.
    /// </summary>
    [Fact]
    public async Task Controller_UsesAppleScriptOnMacOs()
    {
        var runner = Substitute.For<IProcessRunner>();
        runner.RunAsync(default!, default!, default, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(new ProcessResult(0, string.Empty, string.Empty));
        var options = new AwgCliOptions { ConfigDirectory = _directory.FullName, UseAppleScriptElevation = true };
        var controller = new AwgCliTunnelController(runner, options, isWindows: false);

        await controller.DownAsync("golether0", TestContext.Current.CancellationToken);

        var path = controller.GetConfigPath("golether0");
        var expected = $"do shell script (quoted form of \"awg-quick\" & \" \" & quoted form of \"down\" & \" \" & quoted form of {AwgCliTunnelController.AppleScriptString(path)}) with administrator privileges";
        await runner.Received(1).RunAsync("osascript", Arg.Is<IReadOnlyList<string>>(a => a.SequenceEqual(new[] { "-e", expected })), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
        Assert.Equal("\"a\\\"b\\\\c\"", AwgCliTunnelController.AppleScriptString("a\"b\\c"));
    }

    /// <summary>
    /// Writes a configuration file.
    /// </summary>
    /// <param name="name">The interface name.</param>
    /// <returns>The path.</returns>
    private string WriteConfig(string name)
    {
        var path = Path.Combine(_directory.FullName, name + ".conf");
        File.WriteAllText(path, "[Interface]");
        return path;
    }
}
