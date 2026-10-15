namespace Golether.Media.Conference.GStreamer;

/// <summary>
/// The signaling kinds exchanged between peers.
/// </summary>
internal static class SignalKinds
{
    /// <summary>
    /// An SDP offer.
    /// </summary>
    public const string OFFER = "offer";

    /// <summary>
    /// An SDP answer.
    /// </summary>
    public const string ANSWER = "answer";

    /// <summary>
    /// An ICE candidate: <c>&lt;m-line index&gt;\n&lt;candidate&gt;</c>.
    /// </summary>
    public const string CANDIDATE = "candidate";
}
