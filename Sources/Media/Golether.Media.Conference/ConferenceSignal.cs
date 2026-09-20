using Golether.Core.Identity;

namespace Golether.Media.Conference;

/// <summary>
/// Signaling data the conferencing backend exchanges with a peer over the authenticated control stream
/// (SDP offers and answers, ICE candidates).
/// </summary>
/// <param name="PeerId">The peer.</param>
/// <param name="Kind">The kind, for example <c>offer</c>, <c>answer</c>, <c>candidate</c>.</param>
/// <param name="Payload">The backend-specific payload.</param>
public sealed record ConferenceSignal(PeerId PeerId, string Kind, string Payload);
