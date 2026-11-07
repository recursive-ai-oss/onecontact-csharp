using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RecursiveAI.OneContact.Serialization;

/// <summary>
/// Reads and writes a <see cref="PhoneNumber"/> as a JSON string holding its canonical form. No reflection: it works with source-generated
/// serialization and under NativeAOT. A string that does not parse throws <see cref="JsonException"/> whose message names the reason and whose
/// <see cref="Exception.InnerException"/> is the <see cref="ContactParseException"/>.
/// </summary>
/// <remarks>
/// The canonical form is international, so the type's own attribute reads what it writes with no region. To also read national numbers, for example from a
/// client in a known country, add an instance to the serializer options: <c>options.Converters.Add(new PhoneNumberJsonConverter(Region.DE))</c>. Converters in the
/// options take precedence over the attribute. Writing is the same either way. A number read through a different metadata set than the built-in one cannot
/// be restored from JSON: parse the canonical text with your own <see cref="Phones.PhoneNumberParser"/> instead.
/// </remarks>
public sealed class PhoneNumberJsonConverter : JsonConverter<PhoneNumber>
{
    private readonly Region _region;

    /// <summary>Creates a converter that reads only international numbers (those that start with <c>+</c>).</summary>
    public PhoneNumberJsonConverter()
    {
    }

    /// <summary>Creates a converter that reads numbers without a leading <c>+</c> as national numbers of a region.</summary>
    /// <param name="defaultRegion">The region that national numbers belong to; <see cref="Region.Unknown"/> reads international numbers only.</param>
    public PhoneNumberJsonConverter(Region defaultRegion)
    {
        _region = defaultRegion;
    }

    /// <inheritdoc/>
    public override PhoneNumber Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Parse(reader.TokenType == JsonTokenType.String ? reader.GetString() : null, reader.TokenStartIndex);

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, PhoneNumber value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        JsonText.WriteString(writer, value.Canonical);
    }

    /// <inheritdoc/>
    public override PhoneNumber ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Parse(reader.GetString(), reader.TokenStartIndex);

    /// <inheritdoc/>
    public override void WriteAsPropertyName(Utf8JsonWriter writer, [DisallowNull] PhoneNumber value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        JsonText.WritePropertyName(writer, value.Canonical);
    }

    private PhoneNumber Parse(string? text, long tokenStart)
    {
        if (text is null)
        {
            throw JsonText.Invalid("PhoneNumber", null, tokenStart);
        }

        try
        {
            return PhoneNumber.Parse(text, _region);
        }
        catch (ContactParseException e)
        {
            throw JsonText.Invalid("PhoneNumber", e, tokenStart);
        }
    }
}
