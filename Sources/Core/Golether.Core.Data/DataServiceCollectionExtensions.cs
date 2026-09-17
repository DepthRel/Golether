using Golether.Core.Data.Context;
using Golether.Core.Data.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Golether.Core.Data;

/// <summary>
/// Registers the data services.
/// </summary>
public static class DataServiceCollectionExtensions
{
    /// <summary>
    /// Registers the EF Core context factory and the stores for a SQLite database that is already migrated.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <param name="connectionString">The SQLite connection string.</param>
    /// <returns>The same services.</returns>
    public static IServiceCollection AddGoletherData(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services.AddDbContextFactory<GoletherDbContext>(options => options.UseSqlite(connectionString));
        services.AddSingleton<EfStores>();
        services.AddSingleton<IContactStore>(sp => sp.GetRequiredService<EfStores>());
        services.AddSingleton<ITunnelStore>(sp => sp.GetRequiredService<EfStores>());
        services.AddSingleton<ISettingsStore>(sp => sp.GetRequiredService<EfStores>());
        return services;
    }
}
