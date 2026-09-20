namespace Golether.Media.Conference.GStreamer;

/// <summary>
/// Settings of <see cref="GStreamerConferenceMedia"/>.
/// </summary>
public sealed record GStreamerConferenceOptions
{
    /// <summary>
    /// Gets the Golether GStreamer bundle (Windows), or <see langword="null"/> for system libraries.
    /// </summary>
    public string? BundleRoot { get; init; }

    /// <summary>
    /// Gets the plugin registry cache file.
    /// </summary>
    public required string RegistryFile { get; init; }

    /// <summary>
    /// Gets a value indicating whether test patterns replace the camera and microphone (tests, machines without devices).
    /// </summary>
    public bool UseTestSources { get; init; }

    /// <summary>
    /// Gets a value indicating whether the voices are played (tests disable it).
    /// </summary>
    public bool EnablePlayback { get; init; } = true;

    /// <summary>
    /// Gets an optional STUN server, for example <c>stun://stun.example.org:3478</c>.
    /// </summary>
    public string? StunServer { get; init; }

    /// <summary>
    /// Gets a value indicating whether media may flow only through the TURN relay (tests of the relay).
    /// </summary>
    public bool RelayOnly { get; init; }

    /// <summary>
    /// Gets the video bitrate in bits per second (default 600 kbit/s).
    /// </summary>
    public int VideoBitrate { get; init; } = 600_000;

    /// <summary>
    /// Gets the bitrate of the economy camera stream for weak connections (default 150 kbit/s).
    /// </summary>
    public int LowVideoBitrate { get; init; } = 150_000;

    /// <summary>
    /// Gets how often the connection statistics choose the camera stream of each participant (default 2 s);
    /// <see cref="TimeSpan.Zero"/> keeps the full quality for everybody.
    /// </summary>
    public TimeSpan QualityInterval { get; init; } = TimeSpan.FromSeconds(2);
}
