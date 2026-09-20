namespace Golether.Tunnels.AmneziaWG.Packages;

/// <summary>
/// Secrets the host keeps until the answer to an offer arrives. Store them protected.
/// </summary>
/// <param name="OfferId">The offer identifier.</param>
/// <param name="EphemeralPrivateKey">The PKCS#8 ephemeral P-256 private key.</param>
/// <param name="AssignedAddress">The participant address.</param>
/// <param name="ExpiresAt">The expiry time.</param>
public sealed record PendingOfferSecrets(string OfferId, byte[] EphemeralPrivateKey, string AssignedAddress, DateTimeOffset ExpiresAt)
{
    /// <summary>
    /// Returns the record without the private key.
    /// </summary>
    /// <returns>The identifier and address.</returns>
    public override string ToString() => $"PendingOfferSecrets {{ OfferId = {OfferId}, AssignedAddress = {AssignedAddress} }}";
}
