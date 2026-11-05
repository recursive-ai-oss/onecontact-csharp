using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RecursiveAI.OneContact.Serialization;

/// <summary>Reads and writes a <see cref="Region"/> as its two-letter JSON string. No reflection. A bad code throws <see cref="JsonException"/>.</summary>
public sealed class RegionJsonConverter : JsonConverter<Region>
{
    /// <inheritdoc/>
    public override Region Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Parse(reader.TokenType == JsonTokenType.String ? reader.GetString() : null);

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, Region value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.Code);
    }

    /// <inheritdoc/>
    public override Region ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Parse(reader.GetString());

    /// <inheritdoc/>
    public override void WriteAsPropertyName(Utf8JsonWriter writer, [DisallowNull] Region value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WritePropertyName(value.Code);
    }

    private static Region Parse(string? text)
    {
        if (!Region.TryParse(text, out Region value))
        {
            throw new JsonException("The JSON value is not a valid Region.");
        }

        return value;
    }
}
