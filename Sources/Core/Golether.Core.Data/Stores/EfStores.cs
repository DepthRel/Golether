using Golether.Core.Data.Context;
using Golether.Core.Data.Entities;
using Golether.Core.Identity;
using Microsoft.EntityFrameworkCore;

namespace Golether.Core.Data.Stores;

/// <summary>
/// EF Core implementation of the stores. Each call uses its own short-lived context.
/// </summary>
public sealed class EfStores : IContactStore, ITunnelStore, ISettingsStore
{
    /// <summary>
    /// The context factory.
    /// </summary>
    private readonly IDbContextFactory<GoletherDbContext> _factory;

    /// <summary>
    /// The time provider.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="EfStores"/> class.
    /// </summary>
    /// <param name="factory">The context factory.</param>
    /// <param name="timeProvider">The time provider.</param>
    public EfStores(IDbContextFactory<GoletherDbContext> factory, TimeProvider timeProvider)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <inheritdoc />
    public async Task<ContactEntity?> FindAsync(PeerId peerId, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.PeerId == peerId.Value, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ContactEntity>> ListAsync(CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var contacts = await db.Contacts.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        return contacts.OrderByDescending(c => c.LastSeenAt).ToArray();
    }

    /// <inheritdoc />
    public async Task<ContactEntity> TouchAsync(PeerId peerId, string displayName, string? endpoint, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        var now = _timeProvider.GetUtcNow();
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var contact = await db.Contacts.FirstOrDefaultAsync(c => c.PeerId == peerId.Value, cancellationToken).ConfigureAwait(false);
        if (contact is null)
        {
            contact = new ContactEntity { PeerId = peerId.Value, FirstSeenAt = now };
            db.Contacts.Add(contact);
        }

        contact.DisplayName = displayName;
        contact.LastSeenAt = now;
        contact.LastEndpoint = endpoint ?? contact.LastEndpoint;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return contact;
    }

    /// <inheritdoc />
    public async Task<bool> SetTrustedAsync(PeerId peerId, bool trusted, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Contacts.Where(c => c.PeerId == peerId.Value)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.IsTrusted, trusted), cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(PeerId peerId, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Contacts.Where(c => c.PeerId == peerId.Value).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    /// <inheritdoc />
    public async Task<byte[]?> GetHostInterfaceAsync(string interfaceName, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entity = await db.HostTunnelInterfaces.AsNoTracking()
            .FirstOrDefaultAsync(e => e.InterfaceName == interfaceName, cancellationToken).ConfigureAwait(false);
        return entity?.SecretData;
    }

    /// <inheritdoc />
    public async Task SaveHostInterfaceAsync(string interfaceName, byte[] secretData, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(interfaceName);
        ArgumentNullException.ThrowIfNull(secretData);
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entity = await db.HostTunnelInterfaces.FirstOrDefaultAsync(e => e.InterfaceName == interfaceName, cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            db.HostTunnelInterfaces.Add(new HostTunnelInterfaceEntity
            {
                InterfaceName = interfaceName,
                SecretData = secretData,
                CreatedAt = _timeProvider.GetUtcNow(),
            });
        }
        else
        {
            entity.SecretData = secretData;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TunnelEntity> AddAsync(TunnelEntity tunnel, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tunnel);
        var now = _timeProvider.GetUtcNow();
        tunnel.CreatedAt = now;
        tunnel.UpdatedAt = now;
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.Tunnels.Add(tunnel);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return tunnel;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(TunnelEntity tunnel, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tunnel);
        tunnel.UpdatedAt = _timeProvider.GetUtcNow();
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        db.Tunnels.Update(tunnel);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TunnelEntity?> FindByOfferAsync(TunnelRole role, string offerId, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await db.Tunnels.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Role == role && t.OfferId == offerId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TunnelEntity>> ListActiveAsync(CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var tunnels = await db.Tunnels.AsNoTracking().Where(t => t.Status != TunnelStatus.Revoked)
            .OrderByDescending(t => t.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        return tunnels;
    }

    /// <inheritdoc />
    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var setting = await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key, cancellationToken).ConfigureAwait(false);
        return setting?.Value;
    }

    /// <inheritdoc />
    public async Task SetAsync(string key, string value, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var setting = await db.Settings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken).ConfigureAwait(false);
        if (setting is null)
        {
            db.Settings.Add(new SettingEntity { Key = key, Value = value });
        }
        else
        {
            setting.Value = value;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
