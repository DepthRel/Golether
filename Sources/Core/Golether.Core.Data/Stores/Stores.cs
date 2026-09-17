using Golether.Core.Data.Context;
using Golether.Core.Data.Entities;
using Golether.Core.Identity;
using Microsoft.EntityFrameworkCore;

namespace Golether.Core.Data.Stores;

/// <summary>
/// Access to known devices.
/// </summary>
public interface IContactStore
{
    /// <summary>
    /// Returns a contact.
    /// </summary>
    /// <param name="peerId">The device identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The contact, or <see langword="null"/>.</returns>
    Task<ContactEntity?> FindAsync(PeerId peerId, CancellationToken cancellationToken);

    /// <summary>
    /// Lists contacts, most recently seen first.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The contacts.</returns>
    Task<IReadOnlyList<ContactEntity>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Records that a device was seen; creates the contact when unknown.
    /// </summary>
    /// <param name="peerId">The device identifier.</param>
    /// <param name="displayName">The name the device introduced itself with.</param>
    /// <param name="endpoint">The address, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The updated contact.</returns>
    Task<ContactEntity> TouchAsync(PeerId peerId, string displayName, string? endpoint, CancellationToken cancellationToken);

    /// <summary>
    /// Marks a contact as trusted or not.
    /// </summary>
    /// <param name="peerId">The device identifier.</param>
    /// <param name="trusted">The new value.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="false"/> when the contact is unknown.</returns>
    Task<bool> SetTrustedAsync(PeerId peerId, bool trusted, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes a contact.
    /// </summary>
    /// <param name="peerId">The device identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="false"/> when the contact is unknown.</returns>
    Task<bool> DeleteAsync(PeerId peerId, CancellationToken cancellationToken);
}

/// <summary>
/// Access to tunnel records and the host interface.
/// </summary>
public interface ITunnelStore
{
    /// <summary>
    /// Returns the protected data of a host interface.
    /// </summary>
    /// <param name="interfaceName">The interface name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The protected data, or <see langword="null"/>.</returns>
    Task<byte[]?> GetHostInterfaceAsync(string interfaceName, CancellationToken cancellationToken);

    /// <summary>
    /// Stores the protected data of a host interface.
    /// </summary>
    /// <param name="interfaceName">The interface name.</param>
    /// <param name="secretData">The protected data.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the data is saved.</returns>
    Task SaveHostInterfaceAsync(string interfaceName, byte[] secretData, CancellationToken cancellationToken);

    /// <summary>
    /// Adds a tunnel record.
    /// </summary>
    /// <param name="tunnel">The record; <see cref="TunnelEntity.Id"/> is assigned.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The saved record.</returns>
    Task<TunnelEntity> AddAsync(TunnelEntity tunnel, CancellationToken cancellationToken);

    /// <summary>
    /// Updates a tunnel record.
    /// </summary>
    /// <param name="tunnel">The record.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the record is saved.</returns>
    Task UpdateAsync(TunnelEntity tunnel, CancellationToken cancellationToken);

    /// <summary>
    /// Returns a record by offer.
    /// </summary>
    /// <param name="role">The role.</param>
    /// <param name="offerId">The offer identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The record, or <see langword="null"/>.</returns>
    Task<TunnelEntity?> FindByOfferAsync(TunnelRole role, string offerId, CancellationToken cancellationToken);

    /// <summary>
    /// Lists records that are not revoked.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The records, newest first.</returns>
    Task<IReadOnlyList<TunnelEntity>> ListActiveAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Access to application settings.
/// </summary>
public interface ISettingsStore
{
    /// <summary>
    /// Returns a setting.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    Task<string?> GetAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Stores a setting.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the value is saved.</returns>
    Task SetAsync(string key, string value, CancellationToken cancellationToken);
}

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
