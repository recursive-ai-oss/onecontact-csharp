using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RecursiveAI.OneContact.Serialization;

/// <summary>
/// Reads and writes a <see cref="WebAddress"/> as a JSON string holding its canonical form. No reflection: it works with source-generated
/// serialization and under NativeAOT. A string that does not parse throws <see cref="JsonException"/>.
/// </summary>
public sealed class WebAddressJsonConverter : JsonConverter<WebAddress>
{
    /// <inheritdoc/>
    public override WebAddress Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Parse(reader.TokenType == JsonTokenType.String ? reader.GetString() : null);

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, WebAddress value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.Canonical);
    }

    /// <inheritdoc/>
    public override WebAddress ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Parse(reader.GetString());

    /// <inheritdoc/>
    public override void WriteAsPropertyName(Utf8JsonWriter writer, [DisallowNull] WebAddress value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WritePropertyName(value.Canonical);
    }

    private static WebAddress Parse(string? text)
    {
        if (!WebAddress.TryParse(text, out WebAddress value))
        {
            throw new JsonException("The JSON value is not a valid WebAddress.");
        }

        return value;
    }
}
