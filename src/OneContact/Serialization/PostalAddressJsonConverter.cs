using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RecursiveAI.OneContact.Serialization;

/// <summary>
/// Reads and writes a <see cref="PostalAddress"/> as a JSON string holding its canonical form. No reflection: it works with source-generated
/// serialization and under NativeAOT. A string that does not parse throws <see cref="JsonException"/>.
/// </summary>
public sealed class PostalAddressJsonConverter : JsonConverter<PostalAddress>
{
    /// <inheritdoc/>
    public override PostalAddress Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Parse(reader.TokenType == JsonTokenType.String ? reader.GetString() : null);

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, PostalAddress value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.Canonical);
    }

    /// <inheritdoc/>
    public override PostalAddress ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Parse(reader.GetString());

    /// <inheritdoc/>
    public override void WriteAsPropertyName(Utf8JsonWriter writer, [DisallowNull] PostalAddress value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WritePropertyName(value.Canonical);
    }

    private static PostalAddress Parse(string? text)
    {
        if (!PostalAddress.TryParse(text, out PostalAddress value))
        {
            throw new JsonException("The JSON value is not a valid PostalAddress.");
        }

        return value;
    }
}
