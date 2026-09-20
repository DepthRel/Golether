using System.Text.Json.Serialization;

namespace Golether.UI.Services;

/// <summary>
/// The manifest: the releases for the supported systems.
/// </summary>
/// <param name="Windows">The release for Windows.</param>
/// <param name="Linux">The release for Linux.</param>
/// <param name="MacOs">The release for macOS.</param>
public sealed record UpdateManifest(
    [property: JsonPropertyName("windows")] UpdateInfo? Windows,
    [property: JsonPropertyName("linux")] UpdateInfo? Linux,
    [property: JsonPropertyName("macos")] UpdateInfo? MacOs);
