using Golether.Core.Identity;

namespace Golether.Media.Conference;

/// <summary>
/// Handles a data channel message of a participant.
/// </summary>
/// <param name="sender">The backend.</param>
/// <param name="peer">The verified sender.</param>
/// <param name="data">The message, valid only during the call.</param>
public delegate void PeerDataHandler(object? sender, PeerId peer, ReadOnlySpan<byte> data);
