using RecursiveAI.OneContact.Phones.Metadata;

namespace RecursiveAI.OneContact.Ingest.LibPhoneNumber;

/// <summary>
/// Compiles libphonenumber's numbering-plan metadata (table <c>main</c>), short-number metadata (<c>short</c>) and alternate formats
/// (<c>alt</c>). The tables keep the files' own structure with white space removed from pattern text; the rules that give a missing
/// element its inherited value are applied when the library loads the tables, so user-supplied metadata follows the same rules.
/// </summary>
public sealed class LibPhoneNumberSource : IIngestSource
{
    /// <inheritdoc/>
    public string Name => "libphonenumber";

    /// <inheritdoc/>
    public string ToolVersion => "1";

    /// <inheritdoc/>
    public string NoticeText =>
        "libphonenumber metadata\n" +
        "Copyright (C) 2009 The Libphonenumber Authors\n\n" +
        "Licensed under the Apache License, Version 2.0 (the \"License\"); you may not use this file except in compliance with the License.\n" +
        "You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0\n\n" +
        "The phone number data compiled into this package (PhoneNumberMetadata.xml, ShortNumberMetadata.xml and PhoneNumberAlternateFormats.xml of\n" +
        "https://github.com/google/libphonenumber, release v9.0.43) is distributed under the Apache License, Version 2.0. The upstream\n" +
        "files carry no separate NOTICE file. OneContact contains no libphonenumber code.\n";

    /// <inheritdoc/>
    public void Convert(IngestInput input, TableSetWriter tables)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(tables);
        tables.Add("main", Compile(input, "PhoneNumberMetadata.xml"));
        tables.Add("short", Compile(input, "ShortNumberMetadata.xml"));
        tables.Add("alt", Compile(input, "PhoneNumberAlternateFormats.xml"));
    }

    private static byte[] Compile(IngestInput input, string path)
    {
        using MemoryStream xml = new(input.ReadAllBytes(path));
        return PhoneMetadataCodec.Write(PhoneMetadataXml.Read(xml));
    }
}
