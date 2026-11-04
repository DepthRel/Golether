using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Golether.Localization;
using Golether.Security.Secrets;
using Golether.Tunnels.AmneziaWG.Configuration;

namespace Golether.Tunnels.AmneziaWG.Control;

/// <summary>
/// Controls tunnels through the official command-line tools: <c>amneziawg.exe /installtunnelservice</c> on Windows and
/// <c>awg-quick up|down</c> on Linux and macOS. Both require administrator rights.
/// </summary>
public sealed class AwgCliTunnelController : ITunnelController
{
    /// <summary>
    /// The shell that runs <see cref="QuickScript"/>; an absolute path, as the elevation programs want it.
    /// </summary>
    private const string SHELL = "/bin/sh";

    /// <summary>
    /// The process runner.
    /// </summary>
    private readonly IProcessRunner _runner;

    /// <summary>
    /// The options.
    /// </summary>
    private readonly AwgCliOptions _options;

    /// <summary>
    /// The logger.
    /// </summary>
    private readonly ILogger<AwgCliTunnelController> _logger;

    /// <summary>
    /// Whether the controller targets Windows.
    /// </summary>
    private readonly bool _windows;

    /// <summary>
    /// Starts the helper through the UAC prompt.
    /// </summary>
    private readonly IProcessRunner _elevatedRunner;

    /// <summary>
    /// Gives the engine and the configuration a service may be made of, when the application itself runs with
    /// administrator rights on Windows.
    /// </summary>
    private readonly ITunnelEngineGuard? _guard;

    /// <summary>
    /// Gets the name of the program a failed start is reported for.
    /// </summary>
    private string AmneziaWgExecutableName => Path.GetFileName(_options.ResolveWindowsExecutable());

    /// <summary>
    /// Initializes a new instance of the <see cref="AwgCliTunnelController"/> class.
    /// </summary>
    /// <param name="runner">The process runner.</param>
    /// <param name="options">The options.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="isWindows">Overrides the platform (tests); the current platform when <see langword="null"/>.</param>
    /// <param name="elevatedRunner">Starts the helper with administrator rights; <see cref="ElevatedProcessRunner"/> when
    /// <see langword="null"/>.</param>
    /// <param name="guard">Gives protected copies of the engine and the configuration when the application runs with
    /// administrator rights on Windows; without it such an application refuses to change tunnels.</param>
    public AwgCliTunnelController(
        IProcessRunner runner,
        AwgCliOptions options,
        ILogger<AwgCliTunnelController>? logger = null,
        bool? isWindows = null,
        IProcessRunner? elevatedRunner = null,
        ITunnelEngineGuard? guard = null)
    {
        _guard = guard;
        _elevatedRunner = elevatedRunner ?? new ElevatedProcessRunner();
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ConfigDirectory);
        _logger = logger ?? NullLogger<AwgCliTunnelController>.Instance;
        _windows = isWindows ?? OperatingSystem.IsWindows();
    }

    /// <summary>
    /// Refuses to do anything when the tunnel engine is not there. Without this the failure surfaces as a raw
    /// "cannot start process" from somewhere deep inside, which says nothing about what to do.
    /// </summary>
    /// <exception cref="TunnelControlException">The engine is missing.</exception>
    private void EnsureEngine()
    {
        if (_windows && !File.Exists(_options.ResolveWindowsExecutable()))
        {
            throw new TunnelControlException(Texts.Get("Tunnel.Error.EngineMissing"));
        }
    }

    /// <summary>
    /// Returns the configuration file path of an interface (the file name defines the interface name).
    /// </summary>
    /// <param name="interfaceName">The interface name.</param>
    /// <returns>The path.</returns>
    public string GetConfigPath(string interfaceName)
    {
        TunnelNames.Validate(interfaceName);
        return Path.Combine(Path.GetFullPath(_options.ConfigDirectory), interfaceName + ".conf");
    }

    /// <inheritdoc />
    public async Task<string?> CheckAvailabilityAsync(CancellationToken cancellationToken)
    {
        if (_windows)
        {
            return File.Exists(_options.ResolveWindowsExecutable())
                ? null
                : Texts.Get("Tunnel.Availability.EngineMissing");
        }

        try
        {
            await _runner.RunAsync(_options.QuickExecutable, ["--help"], TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (Exception ex) when (ex is Win32Exception or TimeoutException or InvalidOperationException)
        {
            // Not found, not started or hanging: either way the tools cannot be used. The caller only asks whether
            // they are there and must get an answer, not an exception.
            return Texts.Format("Tunnel.Availability.ToolsMissing", _options.QuickExecutable);
        }
    }

    /// <inheritdoc />
    public async Task UpAsync(string interfaceName, AwgConfiguration configuration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        EnsureEngine();
        var path = GetConfigPath(interfaceName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        SecretProtectors.WriteOwnerOnlyFile(path, System.Text.Encoding.UTF8.GetBytes(configuration.Render()));

        if (_windows && _options.HelperExecutable is not null)
        {
            await RunHelperAsync(TunnelHelper.UP_VERB, path, cancellationToken).ConfigureAwait(false);
        }
        else if (_windows)
        {
            await RunGuardedAsync(TunnelHelper.UP_VERB, path, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await RunQuickAsync("up", path, interfaceName, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Tunnel {Interface} is up", interfaceName);
    }

    /// <inheritdoc />
    public async Task DownAsync(string interfaceName, CancellationToken cancellationToken)
    {
        EnsureEngine();
        var path = GetConfigPath(interfaceName);
        if (_windows && _options.HelperExecutable is not null)
        {
            await RunHelperAsync(TunnelHelper.DOWN_VERB, path, cancellationToken).ConfigureAwait(false);
        }
        else if (_windows)
        {
            await RunGuardedAsync(TunnelHelper.DOWN_VERB, path, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await RunQuickAsync("down", path, interfaceName, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Tunnel {Interface} is down", interfaceName);
    }

    /// <summary>
    /// Runs <c>awg-quick</c> as root through <see cref="QuickScript"/>, optionally through the elevation program: on a
    /// copy of the configuration that only root can change and that was checked, never on the file of the data folder.
    /// </summary>
    /// <param name="verb"><c>up</c> or <c>down</c>.</param>
    /// <param name="configPath">The configuration file the application wrote.</param>
    /// <param name="interfaceName">The name of the tunnel.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the script exits.</returns>
    /// <exception cref="TunnelControlException">The configuration was refused or the tool failed.</exception>
    private Task RunQuickAsync(string verb, string configPath, string interfaceName, CancellationToken cancellationToken)
    {
        var arguments = QuickScript.BuildArguments(verb, configPath, interfaceName, _options.QuickExecutable);
        if (_options.UseAppleScriptElevation)
        {
            var command = string.Join(" & \" \" & ", new[] { SHELL }.Concat(arguments).Select(a => "quoted form of " + AppleScriptString(a)));
            return RunAsync("osascript", ["-e", $"do shell script ({command}) with administrator privileges"], cancellationToken, QuickScript.REJECTED);
        }

        return string.IsNullOrWhiteSpace(_options.ElevationCommand)
            ? RunAsync(SHELL, arguments, cancellationToken, QuickScript.REJECTED)
            : RunAsync(_options.ElevationCommand, [SHELL, .. arguments], cancellationToken, QuickScript.REJECTED);
    }

    /// <summary>
    /// Writes a text as an AppleScript string literal.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The literal.</returns>
    public static string AppleScriptString(string text)
        => "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    /// <summary>
    /// Changes a tunnel in this process, which runs with administrator rights: as the helper would, from the same
    /// protected copies, so the service never starts a file of the data folder.
    /// </summary>
    /// <param name="verb">The helper verb.</param>
    /// <param name="configPath">The configuration file.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the tunnel was changed.</returns>
    /// <exception cref="TunnelControlException">No protected copies can be made, or a step failed.</exception>
    private async Task RunGuardedAsync(string verb, string configPath, CancellationToken cancellationToken)
    {
        if (_guard is not { } guard)
        {
            throw new TunnelControlException(Texts.Get("Tunnel.Error.HelperRejected"));
        }

        try
        {
            await Task.Run(() => TunnelHelper.Execute(verb, configPath, _runner, _options.ResolveWindowsExecutable(), guard), cancellationToken).ConfigureAwait(false);
        }
        catch (TunnelControlException ex)
        {
            // The steps leave facts, as the helper does; the words come from the language of the user.
            throw new TunnelControlException(TunnelHelper.DescribeFailure(ex.Message), ex);
        }
        catch (Exception ex) when (ex is Win32Exception or TimeoutException or InvalidOperationException)
        {
            throw new TunnelControlException(Texts.Format("Tunnel.Error.ToolNotStarted", AmneziaWgExecutableName, ex.Message), ex);
        }
    }

    /// <summary>
    /// Changes a tunnel through the elevated helper and reports its error.
    /// </summary>
    /// <param name="verb">The helper verb.</param>
    /// <param name="configPath">The configuration file.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the helper exits.</returns>
    /// <exception cref="TunnelControlException">The prompt was declined or AmneziaWG failed.</exception>
    private async Task RunHelperAsync(string verb, string configPath, CancellationToken cancellationToken)
    {
        var resultPath = Path.Combine(Path.GetDirectoryName(configPath)!, $"helper-{Guid.NewGuid():N}{TunnelHelper.RESULT_EXTENSION}");
        ProcessResult result;
        try
        {
            result = await _elevatedRunner.RunAsync(
                _options.HelperExecutable!, TunnelHelper.BuildArguments(verb, configPath, resultPath), _options.Timeout + _options.Timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ElevatedProcessRunner.CANCELLED)
        {
            throw new TunnelControlException(Texts.Get("Tunnel.Error.ElevationDeclined"), ex);
        }
        catch (Exception ex) when (ex is Win32Exception or TimeoutException or InvalidOperationException)
        {
            throw new TunnelControlException(Texts.Format("Tunnel.Error.HelperNotStarted", ex.Message), ex);
        }

        var detail = File.Exists(resultPath) ? await File.ReadAllTextAsync(resultPath, cancellationToken).ConfigureAwait(false) : string.Empty;
        File.Delete(resultPath);
        if (!result.Succeeded)
        {
            throw new TunnelControlException(result.ExitCode == TunnelHelper.INVALID_ARGUMENTS
                ? Texts.Get("Tunnel.Error.HelperRejected")
                : detail.Length > 0 ? TunnelHelper.DescribeFailure(detail) : Texts.Format("Tunnel.Error.HelperExitCode", result.ExitCode));
        }
    }

    /// <summary>
    /// Runs a tool and converts failures.
    /// </summary>
    /// <param name="fileName">The program.</param>
    /// <param name="arguments">The arguments.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <param name="rejectedExitCode">The exit code that means the configuration was refused.</param>
    /// <returns>A task that completes when the tool exits.</returns>
    /// <exception cref="TunnelControlException">The tool failed.</exception>
    private async Task RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken, int rejectedExitCode)
    {
        ProcessResult result;
        try
        {
            result = await _runner.RunAsync(fileName, arguments, _options.Timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is Win32Exception or TimeoutException or InvalidOperationException)
        {
            throw new TunnelControlException(Texts.Format("Tunnel.Error.ToolNotStarted", Path.GetFileName(fileName), ex.Message), ex);
        }

        if (result.ExitCode == rejectedExitCode)
        {
            throw new TunnelControlException(Texts.Get("Tunnel.Error.ConfigurationRejected"));
        }

        if (!result.Succeeded)
        {
            var detail = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
            throw new TunnelControlException(
                Texts.Format("Tunnel.Error.ToolFailedNeedsAdmin", Path.GetFileName(fileName), result.ExitCode, detail.Trim()));
        }
    }
}
