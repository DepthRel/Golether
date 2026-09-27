namespace Golether.Web.Server.Infrastructure;

/// <summary>
/// The answer of the health page.
/// </summary>
/// <param name="Status">Always <c>ok</c> while the server answers.</param>
/// <param name="Version">The version of the server.</param>
/// <param name="Protocol">The rendezvous protocol version.</param>
/// <param name="UptimeSeconds">How long the server has been running.</param>
/// <param name="Rooms">The rooms held now.</param>
/// <param name="Connections">The TCP connections open now.</param>
/// <param name="MeetingsRequested">The meetings asked for since the start.</param>
/// <param name="MeetingsIntroduced">The meetings where both sides got each other's address.</param>
/// <param name="Refused">The connections and requests refused for exceeding a limit.</param>
/// <param name="StunAnswered">The STUN requests answered.</param>
internal sealed record HealthReport(
    string Status,
    string Version,
    int Protocol,
    long UptimeSeconds,
    int Rooms,
    long Connections,
    long MeetingsRequested,
    long MeetingsIntroduced,
    long Refused,
    long StunAnswered);
