using Golether.Core.Data.Entities;

namespace Golether.Core.Data.Stores;

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
