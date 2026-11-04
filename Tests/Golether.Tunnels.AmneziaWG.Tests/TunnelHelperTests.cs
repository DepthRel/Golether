using System.ComponentModel;
using NSubstitute;
using Golether.Localization;
using Golether.Tunnels.AmneziaWG.Configuration;
using Golether.Tunnels.AmneziaWG.Control;

namespace Golether.Tunnels.AmneziaWG.Tests;

/// <summary>
/// Tests of the elevated tunnel helper and of the controller that starts it.
/// </summary>
public sealed class TunnelHelperTests : IDisposable
{
    /// <summary>
    /// The helper executable used in the tests.
    /// </summary>
    private const string HELPER = @"C:\Golether\app\Golether.exe";

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
            Assert.Equal(TunnelHelper.INVALID_ARGUMENTS, TunnelHelper.Run(args, Substitute.For<IProcessRunner>(), _amneziaWg, PassThrough()));
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

        Assert.Equal(TunnelHelper.SUCCESS, TunnelHelper.Run(TunnelHelper.BuildArguments("up", config, result), runner, _amneziaWg, PassThrough()));
        Assert.False(File.Exists(result));
        runner.Received(1).RunAsync(_amneziaWg, Arg.Is<IReadOnlyList<string>>(a => a.SequenceEqual(new[] { "/installtunnelservice", config })), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());

        Assert.Equal(TunnelHelper.FAILURE, TunnelHelper.Run(TunnelHelper.BuildArguments("up", config, result), runner, _amneziaWg, PassThrough()));
        Assert.Contains("Access is denied", File.ReadAllText(result), StringComparison.Ordinal);
    }

    /// <summary>
    /// The helper is a process of its own that knows nothing about the language of the application: it leaves the facts
    /// of a failure, and the application words them in the language of the user.
    /// </summary>
    [Fact]
    public void HelperFailure_IsWordedByTheApplicationInItsLanguage()
    {
        var config = WriteConfig("golether0");
        var result = Path.Combine(_directory.FullName, "f.result");
        var runner = Substitute.For<IProcessRunner>();
        runner.RunAsync(_amneziaWg, Arg.Any<IReadOnlyList<string>>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(5, string.Empty, "Access is denied"));

        Assert.Equal(TunnelHelper.FAILURE, TunnelHelper.Run(TunnelHelper.BuildArguments("down", config, result), runner, _amneziaWg, PassThrough()));
        var text = File.ReadAllText(result);

        Assert.DoesNotContain("завершился", text, StringComparison.Ordinal);
        Assert.Equal("amneziawg.exe завершился с кодом 5: Access is denied", TunnelHelper.DescribeFailure(text));
        using (Texts.Scope(new Localizer(LanguageCatalog.LoadEmbedded(), "en")))
        {
            Assert.Equal("amneziawg.exe finished with code 5: Access is denied", TunnelHelper.DescribeFailure(text));
        }

        Assert.Equal("Access is denied", TunnelHelper.DescribeFailure("Access is denied"));
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
        elevated.RunAsync(HELPER, Arg.Any<IReadOnlyList<string>>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                helperArgs = call.ArgAt<IReadOnlyList<string>>(1);
                return new ProcessResult(0, string.Empty, string.Empty);
            });
        var options = new AwgCliOptions { ConfigDirectory = _directory.FullName, WindowsExecutable = _amneziaWg, HelperExecutable = HELPER };
        var controller = new AwgCliTunnelController(direct, options, isWindows: true, elevatedRunner: elevated);

        await controller.UpAsync("golether0", HostTunnelInterface.Create().BuildConfiguration([]), token);

        Assert.NotNull(helperArgs);
        Assert.True(TunnelHelper.TryParse(helperArgs, out var verb, out var config, out _));
        Assert.Equal("up", verb);
        Assert.Equal(controller.GetConfigPath("golether0"), config);
        await direct.DidNotReceiveWithAnyArgs().RunAsync(default!, default!, default, Arg.Any<CancellationToken>());

        elevated.RunAsync(HELPER, Arg.Any<IReadOnlyList<string>>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                File.WriteAllText(call.ArgAt<IReadOnlyList<string>>(1)[3], TunnelHelper.EncodeToolFailure("amneziawg.exe", 5, "denied"));
                return new ProcessResult(TunnelHelper.FAILURE, string.Empty, string.Empty);
            });
        var failure = await Assert.ThrowsAsync<TunnelControlException>(() => controller.DownAsync("golether0", token));
        Assert.Contains("кодом 5", failure.Message, StringComparison.Ordinal);
        Assert.Empty(_directory.GetFiles("*.result"));

        elevated.RunAsync(HELPER, Arg.Any<IReadOnlyList<string>>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns<ProcessResult>(_ => throw new Win32Exception(ElevatedProcessRunner.CANCELLED));
        var declined = await Assert.ThrowsAsync<TunnelControlException>(() => controller.DownAsync("golether0", token));
        Assert.Contains("права администратора", declined.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// On macOS the checking script runs through the administrator prompt with every argument quoted: the
    /// configuration of the data folder is only an argument of the script, never handed to awg-quick itself.
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
        var quoted = string.Join(" & \" \" & ", new[] { "/bin/sh" }.Concat(QuickScript.BuildArguments("down", path, "golether0", "awg-quick")).Select(a => "quoted form of " + AwgCliTunnelController.AppleScriptString(a)));
        var expected = $"do shell script ({quoted}) with administrator privileges";
        await runner.Received(1).RunAsync("osascript", Arg.Is<IReadOnlyList<string>>(a => a.SequenceEqual(new[] { "-e", expected })), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
        Assert.StartsWith("do shell script (quoted form of \"/bin/sh\" & \" \" & quoted form of \"-c\"", expected, StringComparison.Ordinal);
        Assert.Equal("\"a\\\"b\\\\c\"", AwgCliTunnelController.AppleScriptString("a\"b\\c"));
    }

    /// <summary>
    /// On Linux the script runs through pkexec, and a configuration the script refuses is reported as such.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Controller_UsesTheCheckingScriptOnLinux()
    {
        var token = TestContext.Current.CancellationToken;
        var runner = Substitute.For<IProcessRunner>();
        runner.RunAsync(default!, default!, default, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(new ProcessResult(QuickScript.REJECTED, string.Empty, string.Empty));
        var options = new AwgCliOptions { ConfigDirectory = _directory.FullName, ElevationCommand = "pkexec" };
        var controller = new AwgCliTunnelController(runner, options, isWindows: false);

        var error = await Assert.ThrowsAsync<TunnelControlException>(() => controller.UpAsync("golether0", HostTunnelInterface.Create().BuildConfiguration([]), token));

        Assert.Equal(Texts.Get("Tunnel.Error.ConfigurationRejected"), error.Message);
        var expected = new[] { "/bin/sh" }.Concat(QuickScript.BuildArguments("up", controller.GetConfigPath("golether0"), "golether0", "awg-quick"));
        await runner.Received(1).RunAsync("pkexec", Arg.Is<IReadOnlyList<string>>(a => a.SequenceEqual(expected)), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The service is made of the protected copies only: the helper installs the configuration and starts the engine
    /// the guard gave, never the files of the data folder, and forgets the configuration when the tunnel goes down.
    /// </summary>
    [Fact]
    public void Helper_UsesTheProtectedCopies()
    {
        var config = WriteConfig("golether0");
        var result = Path.Combine(_directory.FullName, "p.result");
        const string ENGINE = @"C:\Program Files\Golether Tunnel\engine\amneziawg.exe";
        const string PROTECTED_CONFIG = @"C:\Program Files\Golether Tunnel\configurations\golether0.conf";
        var guard = Substitute.For<ITunnelEngineGuard>();
        guard.PrepareEngine(_amneziaWg).Returns(ENGINE);
        guard.PrepareConfiguration("golether0", config).Returns(PROTECTED_CONFIG);
        var runner = Substitute.For<IProcessRunner>();
        runner.RunAsync(default!, default!, default, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(new ProcessResult(0, string.Empty, string.Empty));

        Assert.Equal(TunnelHelper.SUCCESS, TunnelHelper.Run(TunnelHelper.BuildArguments("up", config, result), runner, _amneziaWg, guard));
        Assert.Equal(TunnelHelper.SUCCESS, TunnelHelper.Run(TunnelHelper.BuildArguments("down", config, result), runner, _amneziaWg, guard));

        runner.Received(1).RunAsync(ENGINE, Arg.Is<IReadOnlyList<string>>(a => a.SequenceEqual(new[] { "/installtunnelservice", PROTECTED_CONFIG })), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
        runner.DidNotReceive().RunAsync(_amneziaWg, Arg.Any<IReadOnlyList<string>>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
        guard.Received(1).ForgetConfiguration("golether0");
    }

    /// <summary>
    /// A file of the engine that is not the official one stops the change, and the application says so in the
    /// language of the user.
    /// </summary>
    [Fact]
    public void Helper_StopsOnAChangedEngine()
    {
        var config = WriteConfig("golether0");
        var result = Path.Combine(_directory.FullName, "e.result");
        var guard = Substitute.For<ITunnelEngineGuard>();
        guard.PrepareEngine(Arg.Any<string>()).Returns<string>(_ => throw new TunnelControlException(TunnelHelper.EncodeEngineChanged("wintun.dll")));
        var runner = Substitute.For<IProcessRunner>();

        Assert.Equal(TunnelHelper.FAILURE, TunnelHelper.Run(TunnelHelper.BuildArguments("up", config, result), runner, _amneziaWg, guard));

        Assert.Equal(Texts.Format("Tunnel.Error.EngineChanged", "wintun.dll"), TunnelHelper.DescribeFailure(File.ReadAllText(result)));
        runner.DidNotReceiveWithAnyArgs().RunAsync(default!, default!, default, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// An application that runs with administrator rights changes tunnels from the protected copies as well, and
    /// without a guard it changes none.
    /// </summary>
    /// <returns>A task that completes when the test is done.</returns>
    [Fact]
    public async Task Controller_WithAdministratorRights_UsesTheGuard()
    {
        var token = TestContext.Current.CancellationToken;
        var runner = Substitute.For<IProcessRunner>();
        runner.RunAsync(default!, default!, default, Arg.Any<CancellationToken>()).ReturnsForAnyArgs(new ProcessResult(0, string.Empty, string.Empty));
        var options = new AwgCliOptions { ConfigDirectory = _directory.FullName, WindowsExecutable = _amneziaWg };
        var guard = PassThrough();
        var controller = new AwgCliTunnelController(runner, options, isWindows: true, guard: guard);

        await controller.UpAsync("golether0", HostTunnelInterface.Create().BuildConfiguration([]), token);

        guard.Received(1).PrepareEngine(_amneziaWg);
        guard.Received(1).PrepareConfiguration("golether0", controller.GetConfigPath("golether0"));
        var unguarded = new AwgCliTunnelController(runner, options, isWindows: true);
        await Assert.ThrowsAsync<TunnelControlException>(() => unguarded.DownAsync("golether0", token));
    }

    /// <summary>
    /// A guard that gives the files as they are, for the tests of the steps around it.
    /// </summary>
    /// <returns>The guard.</returns>
    private static ITunnelEngineGuard PassThrough()
    {
        var guard = Substitute.For<ITunnelEngineGuard>();
        guard.PrepareEngine(Arg.Any<string>()).Returns(call => call.Arg<string>());
        guard.PrepareConfiguration(Arg.Any<string>(), Arg.Any<string>()).Returns(call => call.ArgAt<string>(1));
        return guard;
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
