using System.Text.Json;
using System.Text.Json.Serialization;

namespace Golether.Web.Server.Contracts;

/// <summary>
/// The fixed names the server and its monitor share: the service on every system, the local status address and
/// the format of what it answers.
/// </summary>
public static class ServerContract
{
    /// <summary>
    /// The name of the Windows service.
    /// </summary>
    public const string WindowsServiceName = "GoletherServer";

    /// <summary>
    /// The name of the systemd unit on Linux.
    /// </summary>
    public const string SystemdUnit = "golether-server";

    /// <summary>
    /// The label of the launchd job on macOS.
    /// </summary>
    public const string LaunchdLabel = "org.golether.server";

    /// <summary>
    /// The file of the launchd job on macOS.
    /// </summary>
    public const string LaunchdPlist = "/Library/LaunchDaemons/org.golether.server.plist";

    /// <summary>
    /// The status address the server listens on when the settings do not say otherwise.
    /// </summary>
    public const string DefaultStatusUrl = "http://127.0.0.1:47980";

    /// <summary>
    /// The path of the health report.
    /// </summary>
    public const string HealthPath = "/health";

    /// <summary>
    /// The path of the journal of events; <c>?after=N</c> asks for the events after number N.
    /// </summary>
    public const string JournalPath = "/journal";

    /// <summary>
    /// The largest number of events one answer of the journal carries.
    /// </summary>
    public const int JournalPageSize = 1000;

    /// <summary>
    /// Gets the JSON format of the answers: names in camel case, values of enumerations by name.
    /// </summary>
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
