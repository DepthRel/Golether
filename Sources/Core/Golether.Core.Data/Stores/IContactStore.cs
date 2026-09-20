using Golether.Core.Data.Entities;
using Golether.Core.Identity;

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
