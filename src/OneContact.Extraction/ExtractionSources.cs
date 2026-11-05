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

    /// <summary>Reserved for the visible-text reader of a later version; ignored today.</summary>
    Text = 64,
}
