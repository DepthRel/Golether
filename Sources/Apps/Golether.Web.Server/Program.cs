using System.Globalization;
using Golether.Localization;
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
    /// The name of the Windows service the installer registers (Scripts/golether-server.iss).
    /// </summary>
    public const string ServiceName = "GoletherServer";

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
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = PackageLayout.FindConfigDirectory(AppContext.BaseDirectory),
        });
        builder.Services.AddWindowsService(service => service.ServiceName = ServiceName);
        builder.Services.AddSystemd();
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
        app.MapGet("/health", (ServerStatistics statistics) => Results.Json(statistics.Snapshot()));
        app.Run();
        return 0;
    }
}
