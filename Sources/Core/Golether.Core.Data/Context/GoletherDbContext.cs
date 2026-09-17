using Golether.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Golether.Core.Data.Context;

/// <summary>
/// The EF Core context of the Golether database. The schema is owned by the FluentMigrator migrations; the model here
/// only maps to it.
/// </summary>
public sealed class GoletherDbContext : DbContext
{
    /// <summary>
    /// Stores <see cref="DateTimeOffset"/> as Unix milliseconds.
    /// </summary>
    private static readonly ValueConverter<DateTimeOffset, long> UnixMilliseconds = new(
        value => value.ToUnixTimeMilliseconds(),
        value => DateTimeOffset.FromUnixTimeMilliseconds(value));

    /// <summary>
    /// Stores nullable <see cref="DateTimeOffset"/> as Unix milliseconds.
    /// </summary>
    private static readonly ValueConverter<DateTimeOffset?, long?> NullableUnixMilliseconds = new(
        value => value.HasValue ? value.Value.ToUnixTimeMilliseconds() : null,
        value => value.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(value.Value) : null);

    /// <summary>
    /// Initializes a new instance of the <see cref="GoletherDbContext"/> class.
    /// </summary>
    /// <param name="options">The context options.</param>
    public GoletherDbContext(DbContextOptions<GoletherDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Gets the contacts.
    /// </summary>
    public DbSet<ContactEntity> Contacts => Set<ContactEntity>();

    /// <summary>
    /// Gets the host tunnel interfaces.
    /// </summary>
    public DbSet<HostTunnelInterfaceEntity> HostTunnelInterfaces => Set<HostTunnelInterfaceEntity>();

    /// <summary>
    /// Gets the tunnels.
    /// </summary>
    public DbSet<TunnelEntity> Tunnels => Set<TunnelEntity>();

    /// <summary>
    /// Gets the settings.
    /// </summary>
    public DbSet<SettingEntity> Settings => Set<SettingEntity>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ContactEntity>(entity =>
        {
            entity.ToTable("Contacts");
            entity.HasKey(e => e.PeerId);
            entity.Property(e => e.DisplayName).IsRequired();
            entity.Property(e => e.FirstSeenAt).HasConversion(UnixMilliseconds);
            entity.Property(e => e.LastSeenAt).HasConversion(UnixMilliseconds);
        });

        modelBuilder.Entity<HostTunnelInterfaceEntity>(entity =>
        {
            entity.ToTable("HostTunnelInterfaces");
            entity.HasKey(e => e.InterfaceName);
            entity.Property(e => e.SecretData).IsRequired();
            entity.Property(e => e.CreatedAt).HasConversion(UnixMilliseconds);
        });

        modelBuilder.Entity<TunnelEntity>(entity =>
        {
            entity.ToTable("Tunnels");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Role).HasConversion<int>();
            entity.Property(e => e.Status).HasConversion<int>();
            entity.Property(e => e.CreatedAt).HasConversion(UnixMilliseconds);
            entity.Property(e => e.UpdatedAt).HasConversion(UnixMilliseconds);
            entity.Property(e => e.ExpiresAt).HasConversion(NullableUnixMilliseconds);
            entity.HasIndex(e => new { e.Role, e.OfferId }).IsUnique();
        });

        modelBuilder.Entity<SettingEntity>(entity =>
        {
            entity.ToTable("Settings");
            entity.HasKey(e => e.Key);
        });
    }
}
