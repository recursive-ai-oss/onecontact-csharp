using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RecursiveAI.OneContact.Serialization;

/// <summary>
/// Reads and writes a <see cref="PersonName"/> as a JSON string holding its canonical form. No reflection: it works with source-generated
/// serialization and under NativeAOT. A string that does not parse throws <see cref="JsonException"/>.
/// </summary>
public sealed class PersonNameJsonConverter : JsonConverter<PersonName>
{
    /// <inheritdoc/>
    public override PersonName Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Parse(reader.TokenType == JsonTokenType.String ? reader.GetString() : null, reader.TokenStartIndex);

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, PersonName value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        JsonText.WriteString(writer, value.Canonical);
    }

    /// <inheritdoc/>
    public override PersonName ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Parse(reader.GetString(), reader.TokenStartIndex);

    /// <inheritdoc/>
    public override void WriteAsPropertyName(Utf8JsonWriter writer, [DisallowNull] PersonName value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        JsonText.WritePropertyName(writer, value.Canonical);
    }

    private static PersonName Parse(string? text, long tokenStart)
    {
        if (text is null)
        {
            throw JsonText.Invalid("PersonName", null, tokenStart);
        }

        try
        {
            return PersonName.Parse(text);
        }
        catch (ContactParseException e)
        {
            throw JsonText.Invalid("PersonName", e, tokenStart);
        }
    }
}
