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
    private AppServices(AppDataPaths paths, ServiceProvider provider, DeviceIdentity identity)
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
            TimeProvider.System);
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

        var services = new ServiceCollection()
            .AddLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Information);
                logging.AddDebug();
                logging.AddSimpleConsole(options => options.SingleLine = true);
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
        return new AppServices(paths, services, store.LoadOrCreate());
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
            userName => new TunnelDialogViewModel(Tunnels, dialogs, userName),
            Identity.PeerId.ToShortString(),
            Conference,
            Components,
            Player);
    }

    /// <summary>
    /// Leaves the session and releases resources.
    /// </summary>
    /// <returns>A task that completes when everything is released.</returns>
    public async ValueTask DisposeAsync()
    {
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
        return new AwgCliOptions
        {
            ConfigDirectory = paths.TunnelsDirectory,
            HelperExecutable = OperatingSystem.IsWindows() && !privileged ? Environment.ProcessPath : null,
            ElevationCommand = OperatingSystem.IsLinux() && !privileged ? "pkexec" : null,
            UseAppleScriptElevation = OperatingSystem.IsMacOS() && !privileged,
        };
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
