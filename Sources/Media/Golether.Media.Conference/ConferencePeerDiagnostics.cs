using Golether.Core.Identity;

namespace Golether.Media.Conference;

/// <summary>
/// What is really happening on the connection with one participant. Counters say more than a state: a connection
/// that is verified but has sent no video means the camera produced nothing, while zero received means the other
/// side sends nothing.
/// </summary>
/// <param name="Peer">The participant.</param>
/// <param name="Verified">Whether the certificate was checked and media is allowed through.</param>
/// <param name="DataReady">Whether the data channel is open.</param>
/// <param name="Quality">The camera stream this participant gets.</param>
/// <param name="VideoSent">Camera frames sent to the participant.</param>
/// <param name="VideoReceived">Camera frames received from the participant.</param>
/// <param name="AudioSent">Voice frames sent to the participant.</param>
/// <param name="AudioReceived">Voice frames received from the participant.</param>
public sealed record ConferencePeerDiagnostics(
    PeerId Peer,
    bool Verified,
    bool DataReady,
    VideoQuality Quality,
    long VideoSent,
    long VideoReceived,
    long AudioSent,
    long AudioReceived);
