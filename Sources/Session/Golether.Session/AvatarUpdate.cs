using Golether.Core.Identity;

namespace Golether.Session;

/// <summary>
/// The avatar a participant shows while the camera is off, as it reached this device through the host.
/// </summary>
/// <param name="Peer">The participant the avatar belongs to.</param>
/// <param name="Image">The PNG bytes, or <see langword="null"/> when the participant cleared the avatar.</param>
/// <param name="IsLocal">Whether the avatar is this device's own.</param>
public sealed record AvatarUpdate(PeerId Peer, byte[]? Image, bool IsLocal);
