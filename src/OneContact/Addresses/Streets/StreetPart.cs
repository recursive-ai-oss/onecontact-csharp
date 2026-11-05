using System.Text;

namespace RecursiveAI.OneContact.Addresses.Streets;

/// <summary>The role of one piece of a street line (the delivery address line of USPS Publication 28 section 231, the civic address of Canada Post section 4.4).</summary>
public enum StreetPartRole
{
    /// <summary>The primary address number: <c>123</c>, <c>112-10</c> (Queens style), <c>N6W23001</c>, <c>123A</c>, or in Canada <c>123</c> after a unit prefix.</summary>
    HouseNumber = 0,

    /// <summary>A Canadian unit number written before the civic number (<c>10</c> of <c>10-123 MAIN ST</c>); its standard form ends in a hyphen.</summary>
    UnitPrefix = 1,

    /// <summary>A fraction after the house number: <c>1/2</c>.</summary>
    Fraction = 2,

    /// <summary>A directional before the street name: <c>N</c>, <c>NE</c>.</summary>
    PreDirectional = 3,

    /// <summary>The street name.</summary>
    StreetName = 4,

    /// <summary>The street suffix (<c>ST</c>) or type (<c>RUE</c>).</summary>
    StreetType = 5,

    /// <summary>A directional after the street name or suffix.</summary>
    PostDirectional = 6,

    /// <summary>A secondary unit designator: <c>APT</c>, <c>STE</c>, <c>#</c>.</summary>
    UnitDesignator = 7,

    /// <summary>The number or letter that follows a unit designator.</summary>
    UnitNumber = 8,

    /// <summary>A post office box designator: <c>PO BOX</c>, <c>CP</c>.</summary>
    BoxDesignator = 9,

    /// <summary>The post office box number.</summary>
    BoxNumber = 10,

    /// <summary>A rural route or highway contract route designator: <c>RR</c>, <c>HC</c>.</summary>
    RouteDesignator = 11,

    /// <summary>The route number.</summary>
    RouteNumber = 12,

    /// <summary>The words of a general delivery line: <c>GENERAL DELIVERY</c>, <c>GD</c>, <c>PR</c>.</summary>
    GeneralDelivery = 13,

    /// <summary>A station or retail postal outlet designator: <c>STN</c>, <c>RPO</c>, <c>SUCC</c>.</summary>
    StationDesignator = 14,

    /// <summary>The station or outlet name.</summary>
    StationName = 15,

    /// <summary>A military delivery designator: <c>PSC</c>, <c>UNIT</c>, <c>CPR</c>, <c>OPC</c>, <c>UPR</c>.</summary>
    MilitaryDesignator = 16,

    /// <summary>The unit or box numbers of a military line.</summary>
    MilitaryNumber = 17,

    /// <summary>Text on the line the rules could not place; it is kept as written.</summary>
    Other = 18,

    /// <summary>A Canadian additional-address word such as <c>SITE</c> or <c>COMP</c>, or a private mailbox designator (<c>PMB</c>).</summary>
    AdditionalDesignator = 19,

    /// <summary>The number that follows an additional designator.</summary>
    AdditionalNumber = 20,
}

/// <summary>
/// One piece of a street line: what it is, the text as written, the standardized text, where it was in the input and which rule decided it.
/// </summary>
/// <param name="Role">What the piece is.</param>
/// <param name="Text">The text as written in the input (a rewritten piece such as a number moved to the front still reports its original text).</param>
/// <param name="Standard">The text after the authority's token rewrites, upper case, as printed in the <c>Postal</c> rendering.</param>
/// <param name="Span">Where the piece is in the original input, in UTF-16 code units.</param>
/// <param name="Rule">The rule that decided it, named for the guide clause it follows, such as <c>pub28-233.22</c> or <c>canada-post-4.4-unit-number</c>.</param>
public readonly record struct StreetPart(StreetPartRole Role, string Text, string Standard, SourceSpan Span, string Rule);

/// <summary>What kind of delivery line a street line is.</summary>
public enum StreetLineKind
{
    /// <summary>A street address with a house number or a named place.</summary>
    Street = 0,

    /// <summary>A post office box.</summary>
    PostOfficeBox = 1,

    /// <summary>A rural route (<c>RR 4 BOX 87A</c>).</summary>
    RuralRoute = 2,

    /// <summary>A highway contract route (<c>HC 68 BOX 23A</c>).</summary>
    HighwayContractRoute = 3,

    /// <summary>General delivery.</summary>
    GeneralDelivery = 4,

    /// <summary>An overseas military line (<c>PSC 3022 BOX 684</c>, <c>UNIT 9900 BOX 0500</c>).</summary>
    Military = 5,

    /// <summary>Only a secondary unit (<c>APT 10</c>), printed on the line above the street line.</summary>
    SecondaryOnly = 6,

    /// <summary>A Canadian additional-address line (<c>SITE 6 COMP 10</c>).</summary>
    AdditionalAddress = 7,
}

/// <summary>A delivery line broken into parts. Immutable.</summary>
public sealed class StreetLine
{
    internal StreetLine(StreetLineKind kind, IReadOnlyList<StreetPart> parts)
    {
        Kind = kind;
        Parts = parts;
        int start = int.MaxValue;
        int end = 0;
        foreach (StreetPart part in parts)
        {
            if (!part.Span.IsEmpty)
            {
                start = Math.Min(start, part.Span.Start);
                end = Math.Max(end, part.Span.End);
            }
        }

        Span = start == int.MaxValue ? default : new SourceSpan(start, end - start);
    }

    /// <summary>Gets what kind of line this is.</summary>
    public StreetLineKind Kind { get; }

    /// <summary>Gets the pieces in the order they are printed.</summary>
    public IReadOnlyList<StreetPart> Parts { get; }

    /// <summary>Gets the stretch of the original input the line covers.</summary>
    public SourceSpan Span { get; }

    /// <summary>Gets the first part with a role, or <see langword="null"/>.</summary>
    /// <param name="role">The role to look for.</param>
    /// <returns>The part, if any.</returns>
    public StreetPart? Find(StreetPartRole role)
    {
        foreach (StreetPart part in Parts)
        {
            if (part.Role == role)
            {
                return part;
            }
        }

        return null;
    }

    /// <summary>Gets the standardized text of the line in the postal authority's style: the standard forms of the parts, one space between them (none after a part ending in a hyphen).</summary>
    /// <returns>The line.</returns>
    public string ToPostalString()
    {
        StringBuilder sb = new();
        bool glue = true;
        foreach (StreetPart part in Parts)
        {
            // A part always has its standard text; falling back to the text as written keeps a rule gap from ever becoming an exception.
            string standard = part.Standard ?? part.Text ?? string.Empty;
            if (standard.Length == 0)
            {
                continue;
            }

            if (!glue)
            {
                sb.Append(' ');
            }

            sb.Append(standard);
            glue = standard[^1] == '-';
        }

        return sb.ToString();
    }
}
