using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RecursiveAI.OneContact.Serialization;

/// <summary>
/// Reads and writes a <see cref="CompanyName"/> as a JSON string holding its canonical form. No reflection: it works with source-generated
/// serialization and under NativeAOT. A string that does not parse throws <see cref="JsonException"/>.
/// </summary>
public sealed class CompanyNameJsonConverter : JsonConverter<CompanyName>
{
    /// <inheritdoc/>
    public override CompanyName Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Parse(reader.TokenType == JsonTokenType.String ? reader.GetString() : null, reader.TokenStartIndex);

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, CompanyName value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        JsonText.WriteString(writer, value.Canonical);
    }

    /// <inheritdoc/>
    public override CompanyName ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Parse(reader.GetString(), reader.TokenStartIndex);

    /// <inheritdoc/>
    public override void WriteAsPropertyName(Utf8JsonWriter writer, [DisallowNull] CompanyName value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        JsonText.WritePropertyName(writer, value.Canonical);
    }

    private static CompanyName Parse(string? text, long tokenStart)
    {
        if (text is null)
        {
            throw JsonText.Invalid("CompanyName", null, tokenStart);
        }

        try
        {
            return CompanyName.Parse(text);
        }
        catch (ContactParseException e)
        {
            throw JsonText.Invalid("CompanyName", e, tokenStart);
        }
    }
}
