namespace RecursiveAI.OneContact.Extraction;

/// <summary>Which readers an extraction runs.</summary>
[Flags]
public enum ExtractionSources
{
    /// <summary>No reader.</summary>
    None = 0,

    /// <summary>JSON-LD blocks.</summary>
    JsonLd = 1,

    /// <summary>Microdata.</summary>
    Microdata = 2,

    /// <summary>RDFa Lite.</summary>
    Rdfa = 4,

    /// <summary>microformats2 <c>h-card</c> and classic hCard.</summary>
    HCard = 8,

    /// <summary><c>mailto:</c> and <c>tel:</c> links.</summary>
    Links = 16,

    /// <summary><c>address</c> elements.</summary>
    AddressElements = 32,

    /// <summary>Every markup reader.</summary>
    Markup = JsonLd | Microdata | Rdfa | HCard | Links | AddressElements,

    /// <summary>The visible text of a page, or a plain string: email addresses, web addresses, phone numbers and United States and Canadian addresses found by rule, with obfuscated forms decoded.</summary>
    Text = 64,

    /// <summary>Every reader: the markup readers and the text rules.</summary>
    All = Markup | Text,
}
