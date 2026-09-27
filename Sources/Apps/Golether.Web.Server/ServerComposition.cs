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
}
