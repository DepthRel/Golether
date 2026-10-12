using Golether.Web.Server.Infrastructure;
using Golether.Web.Server.Options;
using Golether.Web.Server.Rendezvous;
using Golether.Web.Server.Stun;

namespace Golether.Web.Server;

/// <summary>
/// Registers the services of the rendezvous server.
/// </summary>
internal static class ServerComposition
{
    /// <summary>
    /// Adds the rooms, the meetings, the TCP listener and, when enabled, the STUN responder.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <param name="options">The validated options.</param>
    /// <returns>The services.</returns>
    public static IServiceCollection AddRendezvousServer(this IServiceCollection services, RendezvousServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        services.AddSingleton(options);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<RoomRegistry>();
        services.AddSingleton<MeetingBroker>();
        services.AddSingleton(provider =>
        {
            var statistics = new ServerStatistics(provider.GetRequiredService<TimeProvider>());
            var rooms = provider.GetRequiredService<RoomRegistry>();
            statistics.CountRooms = () => rooms.Count;
            return statistics;
        });
        services.AddSingleton<ConnectionHandler>();
        services.AddSingleton<RendezvousListener>();
        services.AddHostedService(provider => provider.GetRequiredService<RendezvousListener>());
        if (options.EnableStun)
        {
            services.AddSingleton<StunResponder>();
            services.AddHostedService(provider => provider.GetRequiredService<StunResponder>());
        }

        return services;
    }

    /// <summary>
    /// Makes every line of the console log start with the date, the time and the offset from UTC: a log read days
    /// later, or next to the log of a device in another time zone, must say when things happened. A format the
    /// administrator set (<c>Logging:Console:FormatterOptions:TimestampFormat</c>) is kept.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <returns>The same services.</returns>
    public static IServiceCollection AddLogTimestamps(this IServiceCollection services)
    {
        services.PostConfigure<Microsoft.Extensions.Logging.Console.SimpleConsoleFormatterOptions>(
            options => options.TimestampFormat ??= LogTimestampFormat);
        return services;
    }

    /// <summary>
    /// The default stamp of a log line, for example <c>2026-10-11 16:14:12.250 +07:00</c>.
    /// </summary>
    private const string LogTimestampFormat = "yyyy-MM-dd HH:mm:ss.fff zzz ";
}
