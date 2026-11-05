using System.Text.Json.Serialization;

namespace RecursiveAI.OneContact.Serialization;

/// <summary>
/// A source-generated JSON context for the contact value types, for trimmed and NativeAOT applications. Each type is written as
/// a string holding its canonical form. Combine it with your own context by listing it in <c>TypeInfoResolverChain</c>, or
/// declare your own context with <c>[JsonSerializable]</c> entries for your types: the converters are attached to the types, so
/// no registration is needed either way.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(PhoneNumber))]
[JsonSerializable(typeof(EmailAddress))]
[JsonSerializable(typeof(WebAddress))]
[JsonSerializable(typeof(PostalAddress))]
[JsonSerializable(typeof(PersonName))]
[JsonSerializable(typeof(CompanyName))]
[JsonSerializable(typeof(Region))]
public sealed partial class ContactJsonContext : JsonSerializerContext
{
}
