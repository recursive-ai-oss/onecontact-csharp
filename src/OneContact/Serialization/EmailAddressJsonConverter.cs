using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RecursiveAI.OneContact.Serialization;

/// <summary>
/// Reads and writes a <see cref="EmailAddress"/> as a JSON string holding its canonical form. No reflection: it works with source-generated
/// serialization and under NativeAOT. A string that does not parse throws <see cref="JsonException"/>.
/// </summary>
public sealed class EmailAddressJsonConverter : JsonConverter<EmailAddress>
{
    /// <inheritdoc/>
    public override EmailAddress Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Parse(reader.TokenType == JsonTokenType.String ? reader.GetString() : null);

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, EmailAddress value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        JsonText.WriteString(writer, value.Canonical);
    }

    /// <inheritdoc/>
    public override EmailAddress ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Parse(reader.GetString());

    /// <inheritdoc/>
    public override void WriteAsPropertyName(Utf8JsonWriter writer, [DisallowNull] EmailAddress value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        JsonText.WritePropertyName(writer, value.Canonical);
    }

    private static EmailAddress Parse(string? text)
    {
        if (!EmailAddress.TryParse(text, out EmailAddress value))
        {
            throw new JsonException("The JSON value is not a valid EmailAddress.");
        }

        return value;
    }
}
