namespace RecursiveAI.OneContact.Extraction.Records;

/// <summary>What a <see cref="ContactRecord"/> describes.</summary>
public enum ContactRecordKind
{
    /// <summary>One person.</summary>
    Person = 1,

    /// <summary>A company or other organization (schema.org <c>Organization</c> and its subtypes, <c>LocalBusiness</c> included).</summary>
    Organization = 2,
}

/// <summary>
/// Which field of a record a value fills. New kinds (a job title, for example) may be added in later versions, so code that switches on
/// this should keep a default branch.
/// </summary>
public enum FieldKind
{
    /// <summary>The record's own name: a person's full name or an organization's name. Normalized as a <see cref="PersonName"/> or a <see cref="CompanyName"/>.</summary>
    Name = 1,

    /// <summary>The organization a person works for, by name. Normalized as a <see cref="CompanyName"/>.</summary>
    Organization = 2,

    /// <summary>A telephone number. Normalized as a <see cref="PhoneNumber"/>.</summary>
    Phone = 3,

    /// <summary>An email address. Normalized as an <see cref="EmailAddress"/>.</summary>
    Email = 4,

    /// <summary>A website: a <c>url</c> or <c>sameAs</c> link. Normalized as a <see cref="WebAddress"/>.</summary>
    Website = 5,

    /// <summary>A postal address. Normalized as a <see cref="PostalAddress"/>.</summary>
    Address = 6,
}

/// <summary>Where in the page a field was read. Later versions add readers for <see cref="Text"/>; the member is reserved now.</summary>
public enum FieldSource
{
    /// <summary>A <c>script type="application/ld+json"</c> block.</summary>
    JsonLd = 1,

    /// <summary>HTML microdata (<c>itemscope</c>, <c>itemprop</c>).</summary>
    Microdata = 2,

    /// <summary>RDFa Lite (<c>vocab</c>, <c>typeof</c>, <c>property</c>).</summary>
    Rdfa = 3,

    /// <summary>A microformats2 <c>h-card</c> or a classic hCard (<c>vcard</c>).</summary>
    HCard = 4,

    /// <summary>A <c>mailto:</c> or <c>tel:</c> link.</summary>
    Link = 5,

    /// <summary>An <c>address</c> element.</summary>
    AddressElement = 6,

    /// <summary>Visible text read by a model. Not produced by the markup readers.</summary>
    Text = 7,
}

/// <summary>Facts about how a field was read. Like the flags of the value types, they report and never change a value.</summary>
[Flags]
public enum ExtractedFieldFacts
{
    /// <summary>No flags.</summary>
    None = 0,

    /// <summary>The markup that carried the value is hidden from view (a <c>hidden</c> attribute, <c>aria-hidden="true"</c> or an inline <c>display:none</c> or <c>visibility:hidden</c> on it or an ancestor). Structured markup is read anyway: it is meant for machines.</summary>
    Hidden = 1,

    /// <summary>The value could not be normalized (see <see cref="ExtractedField.Issue"/>); <see cref="ExtractedField.Normalized"/> is empty and <see cref="ExtractedField.Text"/> is all there is.</summary>
    Unnormalized = 2,

    /// <summary>Normalizing needed a region (for a national phone number or a free-text address) that the markup did not give, and one was taken from the record's address country, the page's <c>lang</c> region or <see cref="ContactExtractor.DefaultRegion"/>. The value is only as certain as that region.</summary>
    RegionInferred = 4,

    /// <summary>Another field of the same record disagrees with this one (see <see cref="ContactRecord.Disagreements"/>). Both are kept.</summary>
    Disputed = 8,

    /// <summary>The text was put together from several pieces of markup (a given and a family name, or address parts), so <see cref="ExtractedField.Raw"/> spans all of them and <see cref="ExtractedField.Text"/> differs from it.</summary>
    Composed = 16,

    /// <summary>Reserved: the value was decoded from an obfuscated form. Set by the obfuscation decoder in a later version.</summary>
    Deobfuscated = 32,
}

/// <summary>How two records relate. Every link is stored in both directions.</summary>
public enum RecordLinkKind
{
    /// <summary>This person works for the target organization (schema.org <c>worksFor</c>, or the organization named in the same h-card).</summary>
    WorksFor = 1,

    /// <summary>The target person works for this organization (<c>employee</c>, <c>member</c>).</summary>
    HasMember = 2,

    /// <summary>The target organization is this one's parent (<c>parentOrganization</c>).</summary>
    ParentOrganization = 3,

    /// <summary>The target organization is a part of this one (<c>subOrganization</c>, <c>department</c>).</summary>
    SubOrganization = 4,
}

/// <summary>What the unit of <see cref="SourceSpan"/> offsets in an <see cref="ExtractResult"/> is.</summary>
public enum OffsetUnit
{
    /// <summary>Bytes of the original input (the input was bytes, or a document parsed from bytes).</summary>
    Bytes = 1,

    /// <summary>UTF-16 code units of the original string.</summary>
    Chars = 2,
}

/// <summary>Why a reader gave up on part of a page. The rest of the page is still read.</summary>
public enum ExtractIssueKind
{
    /// <summary>A JSON-LD block is not valid JSON, or nests deeper than the reader allows. Nothing is taken from that block.</summary>
    MalformedJsonLd = 1,

    /// <summary>A work budget was used up (items visited or fields produced); the result is what was read until then.</summary>
    LimitExceeded = 2,
}

/// <summary>A problem a reader met on the page (as opposed to a problem with one value, which sits on the field).</summary>
/// <param name="Kind">What went wrong.</param>
/// <param name="Source">The reader that met it.</param>
/// <param name="Span">Where, in the units of <see cref="ExtractResult.OffsetUnit"/>; empty when not known.</param>
/// <param name="Message">A short explanation.</param>
public readonly record struct ExtractIssue(ExtractIssueKind Kind, FieldSource Source, SourceSpan Span, string Message);
