using System.Text.Json.Serialization;

namespace Golether.UI.Services;

/// <summary>
/// One release described by the update manifest.
/// </summary>
/// <param name="Version">The version, for example <c>0.2.0</c>.</param>
/// <param name="Notes">What changed, in a few lines.</param>
/// <param name="Url">The address of the package or installer.</param>
/// <param name="Sha256">The SHA-256 of the file, in hexadecimal.</param>
/// <param name="Size">The size in bytes, or 0 when unknown.</param>
public sealed record UpdateInfo(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("notes")] string? Notes,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("size")] long Size);
