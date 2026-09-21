using System.ComponentModel;
using Golether.Localization;
using Golether.Security.Secrets;
using Golether.Tunnels.AmneziaWG.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Golether.Tunnels.AmneziaWG.Control;

/// <summary>
/// Controls tunnels through the official command-line tools: <c>amneziawg.exe /installtunnelservice</c> on Windows and
/// <c>awg-quick up|down</c> on Linux and macOS. Both require administrator rights.
/// </summary>
public sealed class AwgCliTunnelController : ITunnelController
{
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
    /// Initializes a new instance of the <see cref="AwgCliTunnelController"/> class.
    /// </summary>
    /// <param name="runner">The process runner.</param>
    /// <param name="options">The options.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="isWindows">Overrides the platform (tests); the current platform when <see langword="null"/>.</param>
    /// <param name="elevatedRunner">Starts the helper with administrator rights; <see cref="ElevatedProcessRunner"/> when
    /// <see langword="null"/>.</param>
    public AwgCliTunnelController(
        IProcessRunner runner,
        AwgCliOptions options,
        ILogger<AwgCliTunnelController>? logger = null,
        bool? isWindows = null,
        IProcessRunner? elevatedRunner = null)
    {
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
        catch (Win32Exception)
        {
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
            await RunHelperAsync(TunnelHelper.UpVerb, path, cancellationToken).ConfigureAwait(false);
        }
        else if (_windows)
        {
            // Replace an existing service so a changed configuration takes effect.
            await RunAsync(_options.ResolveWindowsExecutable(), ["/uninstalltunnelservice", interfaceName], ignoreFailure: true, cancellationToken).ConfigureAwait(false);
            await RunAsync(_options.ResolveWindowsExecutable(), ["/installtunnelservice", path], ignoreFailure: false, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await RunQuickAsync(["down", path], ignoreFailure: true, cancellationToken).ConfigureAwait(false);
            await RunQuickAsync(["up", path], ignoreFailure: false, cancellationToken).ConfigureAwait(false);
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
            await RunHelperAsync(TunnelHelper.DownVerb, path, cancellationToken).ConfigureAwait(false);
        }
        else if (_windows)
        {
            await RunAsync(_options.ResolveWindowsExecutable(), ["/uninstalltunnelservice", interfaceName], ignoreFailure: false, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await RunQuickAsync(["down", path], ignoreFailure: false, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Tunnel {Interface} is down", interfaceName);
    }

    /// <summary>
    /// Runs <c>awg-quick</c>, optionally through the elevation program.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="ignoreFailure">Whether a non-zero exit code is acceptable.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the tool exits.</returns>
    private Task RunQuickAsync(string[] arguments, bool ignoreFailure, CancellationToken cancellationToken)
    {
        if (_options.UseAppleScriptElevation)
        {
            var command = string.Join(" & \" \" & ", new[] { _options.QuickExecutable }.Concat(arguments).Select(a => "quoted form of " + AppleScriptString(a)));
            return RunAsync("osascript", ["-e", $"do shell script ({command}) with administrator privileges"], ignoreFailure, cancellationToken);
        }

        return string.IsNullOrWhiteSpace(_options.ElevationCommand)
            ? RunAsync(_options.QuickExecutable, arguments, ignoreFailure, cancellationToken)
            : RunAsync(_options.ElevationCommand, [_options.QuickExecutable, .. arguments], ignoreFailure, cancellationToken);
    }

    /// <summary>
    /// Writes a text as an AppleScript string literal.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The literal.</returns>
    public static string AppleScriptString(string text)
        => "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

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
        var resultPath = Path.Combine(Path.GetDirectoryName(configPath)!, $"helper-{Guid.NewGuid():N}{TunnelHelper.ResultExtension}");
        ProcessResult result;
        try
        {
            result = await _elevatedRunner.RunAsync(
                _options.HelperExecutable!, TunnelHelper.BuildArguments(verb, configPath, resultPath), _options.Timeout + _options.Timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ElevatedProcessRunner.Cancelled)
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
            throw new TunnelControlException(result.ExitCode == TunnelHelper.InvalidArguments
                ? Texts.Get("Tunnel.Error.HelperRejected")
                : detail.Length > 0 ? TunnelHelper.DescribeFailure(detail) : Texts.Format("Tunnel.Error.HelperExitCode", result.ExitCode));
        }
    }

    /// <summary>
    /// Runs a tool and converts failures.
    /// </summary>
    /// <param name="fileName">The program.</param>
    /// <param name="arguments">The arguments.</param>
    /// <param name="ignoreFailure">Whether a non-zero exit code is acceptable.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the tool exits.</returns>
    /// <exception cref="TunnelControlException">The tool failed.</exception>
    private async Task RunAsync(string fileName, IReadOnlyList<string> arguments, bool ignoreFailure, CancellationToken cancellationToken)
    {
        ProcessResult result;
        try
        {
            result = await _runner.RunAsync(fileName, arguments, _options.Timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is Win32Exception or TimeoutException or InvalidOperationException)
        {
            if (ignoreFailure)
            {
                return;
            }

            throw new TunnelControlException(Texts.Format("Tunnel.Error.ToolNotStarted", Path.GetFileName(fileName), ex.Message), ex);
        }

        if (!result.Succeeded && !ignoreFailure)
        {
            var detail = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
            throw new TunnelControlException(
                Texts.Format("Tunnel.Error.ToolFailedNeedsAdmin", Path.GetFileName(fileName), result.ExitCode, detail.Trim()));
        }
    }
}
