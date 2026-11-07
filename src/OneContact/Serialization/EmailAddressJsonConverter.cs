using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RecursiveAI.OneContact.Serialization;

/// <summary>
/// Reads and writes a <see cref="EmailAddress"/> as a JSON string. An address with no display name is written as its canonical form; one that has a display
/// name is written as <c>"Display Name" &lt;canonical&gt;</c> (a quoted-string display name, RFC 5322 §3.4), so the display name survives a round trip. Both forms read
/// back. As a property name only the canonical form is used. No reflection: it works with source-generated serialization and under NativeAOT. A string that
/// does not parse throws <see cref="JsonException"/> whose message names the reason and whose <see cref="Exception.InnerException"/> is the <see cref="ContactParseException"/>.
/// </summary>
public sealed class EmailAddressJsonConverter : JsonConverter<EmailAddress>
{
    /// <inheritdoc/>
    public override EmailAddress Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Parse(reader.TokenType == JsonTokenType.String ? reader.GetString() : null, reader.TokenStartIndex);

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, EmailAddress value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (string.IsNullOrEmpty(value.DisplayName))
        {
            JsonText.WriteString(writer, value.Canonical);
            return;
        }

        // RFC 5322 §3.2.4: a quoted-string escapes a quotation mark and a backslash with a backslash.
        string quoted = "\"" + value.DisplayName.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
        JsonText.WriteString(writer, quoted + " <" + value.Canonical + ">");
    }

    /// <inheritdoc/>
    public override EmailAddress ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Parse(reader.GetString(), reader.TokenStartIndex);

    /// <inheritdoc/>
    public override void WriteAsPropertyName(Utf8JsonWriter writer, [DisallowNull] EmailAddress value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        JsonText.WritePropertyName(writer, value.Canonical);
    }

    private static EmailAddress Parse(string? text, long tokenStart)
    {
        if (text is null)
        {
            throw JsonText.Invalid("EmailAddress", null, tokenStart);
        }

        try
        {
            return EmailAddress.Parse(text);
        }
        catch (ContactParseException e)
        {
            throw JsonText.Invalid("EmailAddress", e, tokenStart);
        }
    }
}
