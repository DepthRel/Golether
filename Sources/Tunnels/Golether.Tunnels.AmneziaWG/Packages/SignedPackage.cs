using Golether.Core.Identity;

namespace Golether.Tunnels.AmneziaWG.Packages;

/// <summary>
/// A signed package with its verified signer.
/// </summary>
/// <typeparam name="T">The body type.</typeparam>
/// <param name="Body">The body.</param>
/// <param name="Signer">The identifier of the signing device.</param>
public sealed record SignedPackage<T>(T Body, PeerId Signer);
