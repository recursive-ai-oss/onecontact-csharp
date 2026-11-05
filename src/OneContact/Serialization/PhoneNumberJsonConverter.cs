using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RecursiveAI.OneContact.Serialization;

/// <summary>
/// Reads and writes a <see cref="PhoneNumber"/> as a JSON string holding its canonical form. No reflection: it works with source-generated
/// serialization and under NativeAOT. A string that does not parse throws <see cref="JsonException"/>.
/// </summary>
public sealed class PhoneNumberJsonConverter : JsonConverter<PhoneNumber>
{
    /// <inheritdoc/>
    public override PhoneNumber Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Parse(reader.TokenType == JsonTokenType.String ? reader.GetString() : null);

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, PhoneNumber value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        JsonText.WriteString(writer, value.Canonical);
    }

    /// <inheritdoc/>
    public override PhoneNumber ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Parse(reader.GetString());

    /// <inheritdoc/>
    public override void WriteAsPropertyName(Utf8JsonWriter writer, [DisallowNull] PhoneNumber value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        JsonText.WritePropertyName(writer, value.Canonical);
    }

    private static PhoneNumber Parse(string? text)
    {
        if (!PhoneNumber.TryParse(text, out PhoneNumber value))
        {
            throw new JsonException("The JSON value is not a valid PhoneNumber.");
        }

        return value;
    }
}
