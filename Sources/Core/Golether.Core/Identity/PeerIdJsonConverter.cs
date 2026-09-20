using System.Text.Json;
using System.Text.Json.Serialization;

namespace Golether.Core.Identity;

/// <summary>
/// Serializes <see cref="PeerId"/> as a JSON string and validates it on reading.
/// </summary>
public sealed class PeerIdJsonConverter : JsonConverter<PeerId>
{
    /// <inheritdoc />
    public override PeerId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString();
        if (text is null)
        {
            return default;
        }

        return PeerId.TryParse(text, out var id) ? id : throw new JsonException("Invalid peer identifier.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, PeerId value, JsonSerializerOptions options)
    {
        if (value.IsEmpty)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value.Value);
    }
}
