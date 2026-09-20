namespace Golether.Tunnels.AmneziaWG.Packages;

/// <summary>
/// An offer created by the host.
/// </summary>
/// <param name="PackageText">The text to send to the participant.</param>
/// <param name="Secrets">The secrets to keep.</param>
public sealed record HostOffer(string PackageText, PendingOfferSecrets Secrets);
