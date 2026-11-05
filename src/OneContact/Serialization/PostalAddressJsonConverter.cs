using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RecursiveAI.OneContact.Serialization;

/// <summary>
/// Reads and writes a <see cref="PostalAddress"/>. An address parsed from text is written as its canonical string, which parses back to the same address (offsets then index the canonical string);
/// an address made from fields is written as an object holding its canonical form, its fields and its flags, and reads back equal. The default value is written as the empty string, which does not read back (as for every value type). As a property name the canonical
/// string is used and read back by parsing. No reflection: it works with source-generated serialization and under NativeAOT. A value that does not read throws <see cref="JsonException"/>.
/// </summary>
public sealed class PostalAddressJsonConverter : JsonConverter<PostalAddress>
{
    /// <inheritdoc/>
    public override PostalAddress Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.StartObject ? ReadObject(ref reader) : Parse(reader.TokenType == JsonTokenType.String ? reader.GetString() : null);

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, PostalAddress value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        Addresses.AddressParts? parts = value.PartsForJson;
        if (parts is null || value.IsParsedFromText)
        {
            writer.WriteStringValue(value.Canonical);
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("canonical", value.Canonical);
        writer.WriteString("country", parts.Country.IsUnknown ? string.Empty : parts.Country.Code);
        if (parts.Language is not null)
        {
            writer.WriteString("language", parts.Language);
        }

        writer.WriteString("adminArea", parts.AdminArea);
        writer.WriteString("locality", parts.Locality);
        writer.WriteString("dependentLocality", parts.DependentLocality);
        writer.WriteString("postalCode", parts.PostalCode);
        writer.WriteString("sortingCode", parts.SortingCode);
        writer.WriteString("organization", parts.Organization);
        writer.WriteString("recipient", parts.Recipient);
        writer.WriteStartArray("streetLines");
        foreach (string line in parts.StreetLines)
        {
            writer.WriteStringValue(line);
        }

        writer.WriteEndArray();
        writer.WriteNumber("flags", (uint)value.Flags);
        writer.WriteNumber("sourceStart", value.Source.Start);
        writer.WriteNumber("sourceLength", value.Source.Length);
        writer.WriteEndObject();
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

    private static PostalAddress ReadObject(ref Utf8JsonReader reader)
    {
        string canonical = string.Empty, country = string.Empty, admin = string.Empty, locality = string.Empty, dependent = string.Empty;
        string postal = string.Empty, sorting = string.Empty, organization = string.Empty, recipient = string.Empty;
        string? language = null;
        List<string> lines = [];
        uint flags = 0;
        int start = 0, length = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("The JSON value is not a valid PostalAddress.");
            }

            string name = reader.GetString() ?? string.Empty;
            reader.Read();
            switch (name)
            {
                case "canonical": canonical = ReadString(ref reader); break;
                case "country": country = ReadString(ref reader); break;
                case "language": language = ReadString(ref reader); break;
                case "adminArea": admin = ReadString(ref reader); break;
                case "locality": locality = ReadString(ref reader); break;
                case "dependentLocality": dependent = ReadString(ref reader); break;
                case "postalCode": postal = ReadString(ref reader); break;
                case "sortingCode": sorting = ReadString(ref reader); break;
                case "organization": organization = ReadString(ref reader); break;
                case "recipient": recipient = ReadString(ref reader); break;
                case "streetLines":
                    if (reader.TokenType != JsonTokenType.StartArray)
                    {
                        throw new JsonException("The JSON value is not a valid PostalAddress.");
                    }

                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        lines.Add(ReadString(ref reader));
                    }

                    break;
                case "flags": flags = reader.TokenType == JsonTokenType.Number && reader.TryGetUInt32(out uint f) ? f : throw new JsonException("The JSON value is not a valid PostalAddress."); break;
                case "sourceStart": start = reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int st) ? st : throw new JsonException("The JSON value is not a valid PostalAddress."); break;
                case "sourceLength": length = reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int ln) ? ln : throw new JsonException("The JSON value is not a valid PostalAddress."); break;
                default: reader.Skip(); break;
            }
        }

        Region region = default;
        if (country.Length != 0 && !Region.TryParse(country, out region))
        {
            throw new JsonException("The JSON value is not a valid PostalAddress.");
        }

        if (start < 0 || length < 0 || (uint)flags > 0xFFu)
        {
            throw new JsonException("The JSON value is not a valid PostalAddress.");
        }

        var parts = new Addresses.AddressParts(region, language, admin, locality, dependent, postal, sorting, organization, recipient, lines);
        return PostalAddress.Restore(canonical, new SourceSpan(start, length), (AddressFacts)flags, parts);
    }

    private static string ReadString(ref Utf8JsonReader reader) =>
        reader.TokenType == JsonTokenType.String ? reader.GetString() ?? string.Empty : throw new JsonException("The JSON value is not a valid PostalAddress.");
}
