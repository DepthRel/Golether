using System.Globalization;
using Golether.Localization;
using Golether.Web.Server.Contracts;
using Golether.Web.Server.Infrastructure;
using Golether.Web.Server.Options;

namespace Golether.Web.Server;

/// <summary>
/// The entry point of the rendezvous server.
/// </summary>
public static class Program
{
    /// <summary>
    /// The exit code of invalid settings.
    /// </summary>
    private const int InvalidSettings = 2;

    /// <summary>
    /// The name of the Windows service the installer registers (Scripts/Server/golether-server.iss).
    /// </summary>
    public const string ServiceName = ServerContract.WindowsServiceName;

    /// <summary>
    /// Reads the settings, checks them and runs the server until it is stopped (Ctrl+C, SIGTERM, service stop).
    /// </summary>
    /// <param name="args">
    /// Command-line settings in the configuration syntax, for example <c>--Rendezvous:ListenAddress=203.0.113.7</c>.
    /// </param>
    /// <returns>The exit code: 0 after a normal stop, 2 for invalid settings.</returns>
    public static int Main(string[] args)
    {
        // The operator reads the messages in the language of the system; the log stays in English.
        var catalog = LanguageCatalog.LoadEmbedded();
        Texts.Localizer = new Localizer(catalog, catalog.Detect(CultureInfo.CurrentUICulture).Code);

        // The settings are found from the program folder, not from the current one: a service starts in the system
        // folder. A package keeps them in config next to bin (see PackageLayout).
        var configDirectory = PackageLayout.FindConfigDirectory(AppContext.BaseDirectory);

        // A missing settings file is written again with the defaults; when that is not possible (a read-only folder)
        // the server still starts with them: they are the bottom layer of the configuration in any case.
        var settingsFile = DefaultSettings.EnsureFile(configDirectory, out var settingsProblem);
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = configDirectory,
        });
        builder.Configuration.Sources.Insert(0, new Microsoft.Extensions.Configuration.Json.JsonStreamConfigurationSource { Stream = DefaultSettings.Open() });
        builder.Services.AddWindowsService(service => service.ServiceName = ServiceName);
        builder.Services.AddSystemd();
        builder.Services.AddLogTimestamps();
        var options = builder.Configuration.GetSection(RendezvousServerOptions.Section).Get<RendezvousServerOptions>() ?? new RendezvousServerOptions();
        if (options.Validate() is { Count: > 0 } problems)
        {
            Console.Error.WriteLine(Texts.Get("Server.Error.Invalid"));
            foreach (var problem in problems)
            {
                Console.Error.WriteLine("  " + problem);
            }

            return InvalidSettings;
        }

        builder.Services.AddRendezvousServer(options);
        var app = builder.Build();
        var startLog = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(Program));
        var settingsPath = Path.Combine(configDirectory, DefaultSettings.FileName);
        if (settingsFile == SettingsFileState.Created)
        {
            startLog.LogWarning("The settings file {Path} was missing; it was written with the default settings", settingsPath);
        }
        else if (settingsFile == SettingsFileState.Missing)
        {
            startLog.LogWarning("The settings file {Path} is missing and could not be written ({Problem}); the server runs with the default settings", settingsPath, settingsProblem);
        }

        app.MapGet(ServerContract.HealthPath, (ServerStatistics statistics) => Results.Json(statistics.Snapshot()));

        // The journal of events is for the monitor on this computer only, whatever address the status page was
        // given in the settings: it names the addresses of the devices.
        app.MapGet(ServerContract.JournalPath, (HttpContext context, ServerJournal journal, long? after)
            => context.Connection.RemoteIpAddress is { } remote && System.Net.IPAddress.IsLoopback(remote)
                ? Results.Json(journal.Read(after ?? 0), ServerContract.Json)
                : Results.StatusCode(StatusCodes.Status403Forbidden));
        var events = app.Services.GetRequiredService<ServerJournal>();
        var version = app.Services.GetRequiredService<ServerStatistics>().Snapshot().Version;
        // The order of a start, as it happens: starting, then the ports are opened (Listening, StunListening, recorded
        // by the services that open them), then the server is ready.
        events.Record(JournalEvent.ServerStarting, detail: version);
        app.Lifetime.ApplicationStarted.Register(() => events.Record(JournalEvent.ServerStarted));
        app.Lifetime.ApplicationStopping.Register(() => events.Record(JournalEvent.ServerStopping));
        app.Run();
        return 0;
    }
}
