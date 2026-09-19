using Golether.Components.Catalog;
using Golether.Components.Installation;
using Golether.Core.Configuration;
using Golether.Core.Data;
using Golether.Core.Data.Migrations.SQLite.Runner;
using Golether.Core.Data.Stores;
using Golether.Media.Conference;
using Golether.Security.Identity;
using Golether.Security.Secrets;
using Golether.Transports.PortMapping;
using Golether.Tunnels.AmneziaWG.Control;
using Golether.UI.ViewModels;
using Golether.UI.ViewModels.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Golether.UI.Services;

/// <summary>
/// The composition root: data directory, database, device identity, player, sessions and tunnels.
/// </summary>
public sealed class AppServices : IAsyncDisposable
{
    /// <summary>
    /// The service provider.
    /// </summary>
    private readonly ServiceProvider _provider;

    /// <summary>
    /// The dialog service, available once the main window exists.
    /// </summary>
    private IDialogService? _dialogs;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppServices"/> class.
    /// </summary>
    /// <param name="paths">The data paths.</param>
    /// <param name="provider">The service provider.</param>
    /// <param name="identity">The device identity.</param>
    /// <param name="fileLog">The log file writer.</param>
    private AppServices(AppDataPaths paths, ServiceProvider provider, DeviceIdentity identity, FileLogProvider fileLog)
    {
        Paths = paths;
        _provider = provider;
        Identity = identity;
        LoggerFactory = provider.GetRequiredService<ILoggerFactory>();
        Http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("Golether/1.0");
        ComponentsRoot = paths.ComponentsDirectory;
        Components = new ComponentService(
            new ComponentLocator(Path.Combine(AppContext.BaseDirectory, "native"), ComponentsRoot),
            new ComponentInstaller(ComponentsRoot, Http, new ProcessToolRunner(), TimeProvider.System, LoggerFactory.CreateLogger<ComponentInstaller>()));
        Diagnostics = new DiagnosticsWriter(paths.LogsDirectory, Components, identity.PeerId.ToShortString(), () => fileLog.CurrentFile);
        Updates = new UpdateService(Http, logger: LoggerFactory.CreateLogger<UpdateService>());
        Player = new PlayerHost(LoggerFactory, paths.MpvDirectory);
        Conference = new ConferenceHost(LoggerFactory, paths.GStreamerRegistryFile);
        Conference.TryActivate(Components.GetStatus(ComponentId.Conference));
        Components.Installed += (_, id) =>
        {
            if (id == ComponentId.Conference)
            {
                Conference.TryActivate(Components.GetStatus(ComponentId.Conference));
            }
        };
        Dispatcher = new AvaloniaUiDispatcher();
        var protector = provider.GetRequiredService<ISecretProtector>();
        Tunnels = new TunnelWorkflow(
            identity,
            provider.GetRequiredService<ITunnelStore>(),
            protector,
            new AwgCliTunnelController(new ProcessRunner(), CreateTunnelOptions(paths), LoggerFactory.CreateLogger<AwgCliTunnelController>()),
            TimeProvider.System,
            raisedStatePath: Path.Combine(paths.TunnelsDirectory, "raised.txt"));
        Sessions = new SessionService(
            identity,
            Player,
            new DialogAdmissionPrompt(() => _dialogs ?? throw new InvalidOperationException("The main window is not ready."), Dispatcher),
            provider.GetRequiredService<IContactStore>(),
            TimeProvider.System,
            LoggerFactory,
            Conference,
            PortMapper.CreateDefault(Http, LoggerFactory));
    }

    /// <summary>
    /// Gets the data paths.
    /// </summary>
    public AppDataPaths Paths { get; }

    /// <summary>
    /// Gets the writer of diagnostic reports.
    /// </summary>
    public IDiagnosticsWriter Diagnostics { get; private set; } = null!;

    /// <summary>
    /// Gets the update checker.
    /// </summary>
    public IUpdateService Updates { get; private set; } = null!;

    /// <summary>
    /// Gets the HTTP client for component downloads.
    /// </summary>
    public HttpClient Http { get; }

    /// <summary>
    /// Gets the folder with components installed by Golether.
    /// </summary>
    public string ComponentsRoot { get; }

    /// <summary>
    /// Gets the component service.
    /// </summary>
    public ComponentService Components { get; }

    /// <summary>
    /// Gets the logger factory.
    /// </summary>
    public ILoggerFactory LoggerFactory { get; }

    /// <summary>
    /// Gets the device identity.
    /// </summary>
    public DeviceIdentity Identity { get; }

    /// <summary>
    /// Gets the player host.
    /// </summary>
    public PlayerHost Player { get; }

    /// <summary>
    /// Gets the conferencing backend.
    /// </summary>
    public ConferenceHost Conference { get; }

    /// <summary>
    /// Gets the UI dispatcher.
    /// </summary>
    public IUiDispatcher Dispatcher { get; }

    /// <summary>
    /// Gets the tunnel workflow.
    /// </summary>
    public TunnelWorkflow Tunnels { get; }

    /// <summary>
    /// Gets the session service.
    /// </summary>
    public SessionService Sessions { get; }

    /// <summary>
    /// Prepares the data directory, migrates the database and loads the device identity.
    /// </summary>
    /// <returns>The services.</returns>
    /// <exception cref="Exception">Start-up failed; the message explains why.</exception>
    public static AppServices Create()
    {
        var paths = AppDataPaths.Resolve();
        paths.EnsureCreated();
        AdoptLegacyData(paths);
        SqliteDatabaseMigrator.MigrateUp(paths.ConnectionString);

        var fileLog = new FileLogProvider(paths.LogsDirectory);
        var services = new ServiceCollection()
            .AddLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Information);
                logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
                logging.AddDebug();
                logging.AddSimpleConsole(options => options.SingleLine = true);
                logging.AddProvider(fileLog);
            })
            .AddSingleton(TimeProvider.System)
            .AddSingleton(SecretProtectors.CreateDefault())
            .AddGoletherData(paths.ConnectionString)
            .BuildServiceProvider();

        var store = new FileDeviceIdentityStore(
            paths.IdentityDirectory,
            services.GetRequiredService<ISecretProtector>(),
            TimeProvider.System,
            services.GetRequiredService<ILogger<FileDeviceIdentityStore>>());
        return new AppServices(paths, services, store.LoadOrCreate(), fileLog);
    }

    /// <summary>
    /// Connects the dialog service of the main window.
    /// </summary>
    /// <param name="dialogs">The dialog service.</param>
    public void SetDialogs(IDialogService dialogs) => _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));

    /// <summary>
    /// Creates the main window view model.
    /// </summary>
    /// <returns>The view model.</returns>
    public MainWindowViewModel CreateMainViewModel()
    {
        var dialogs = _dialogs ?? throw new InvalidOperationException("Call SetDialogs first.");
        return new MainWindowViewModel(
            Sessions,
            dialogs,
            _provider.GetRequiredService<ISettingsStore>(),
            Dispatcher,
            userName => new TunnelDialogViewModel(Tunnels, dialogs, userName, new ComponentItemViewModel(ComponentId.Tunnel, Components, dialogs)),
            Identity.PeerId.ToShortString(),
            Conference,
            Components,
            Player,
            Diagnostics,
            Updates,
            Path.Combine(Paths.Root, "updates"));
    }

    /// <summary>
    /// Leaves the session and releases resources.
    /// </summary>
    /// <returns>A task that completes when everything is released.</returns>
    public async ValueTask DisposeAsync()
    {
        // The tunnel of Golether lives as long as Golether does. Bringing it down asks for administrator rights, so
        // it is given a limited time and a refusal does not hold the application back.
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await Tunnels.DropRaisedAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LoggerFactory.CreateLogger<AppServices>().LogDebug(ex, "The tunnels were not brought down");
        }

        await Sessions.DisposeAsync().ConfigureAwait(false);
        await Player.DisposeAsync().ConfigureAwait(false);
        await Conference.DisposeAsync().ConfigureAwait(false);
        Identity.Dispose();
        Http.Dispose();
        await _provider.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Chooses how tunnel changes get administrator rights on this platform.
    /// </summary>
    /// <param name="paths">The data paths.</param>
    /// <returns>The options.</returns>
    private static AwgCliOptions CreateTunnelOptions(AppDataPaths paths)
    {
        var privileged = Environment.IsPrivilegedProcess;
        var options = new AwgCliOptions
        {
            ConfigDirectory = paths.TunnelsDirectory,
            HelperExecutable = OperatingSystem.IsWindows() && !privileged ? Environment.ProcessPath : null,
            ElevationCommand = OperatingSystem.IsLinux() && !privileged ? "pkexec" : null,
            UseAppleScriptElevation = OperatingSystem.IsMacOS() && !privileged,
        };
        // Looked up on every call: the component can be installed while the application is running.
        return options with { FindCarriedExecutable = () => FindAmneziaWg(paths) };
    }

    /// <summary>
    /// Returns the AmneziaWG the application carries itself, or <see langword="null"/> to fall back to the one
    /// installed in the system. Used by the application and by its elevated tunnel helper, so both call the same
    /// program.
    /// </summary>
    /// <param name="paths">The data paths.</param>
    /// <returns>The path of <c>amneziawg.exe</c>, or <see langword="null"/>.</returns>
    public static string? FindAmneziaWg(AppDataPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        try
        {
            return new ComponentLocator(Path.Combine(AppContext.BaseDirectory, "native"), paths.ComponentsDirectory)
                .GetStatus(ComponentId.Tunnel).Path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Moves the data of an earlier version from the user profile into the installation, once.
    /// </summary>
    /// <param name="paths">The data paths.</param>
    /// <remarks>
    /// Skipped when the data directory is overridden for development. A failed move leaves the old data in place and
    /// the application starts with a new identity.
    /// </remarks>
    private static void AdoptLegacyData(AppDataPaths paths)
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(AppDataPaths.DataDirectoryVariable)))
        {
            return;
        }

        try
        {
            paths.AdoptLegacyData(AppDataPaths.GetLegacyUserDirectory());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"Golether: the data of an earlier version was not moved: {ex.Message}");
        }
    }
}
