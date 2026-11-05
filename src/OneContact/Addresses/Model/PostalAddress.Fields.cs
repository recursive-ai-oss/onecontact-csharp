using System.Buffers;
using RecursiveAI.OneContact.Addresses;
using RecursiveAI.OneContact.Addresses.Structural;

namespace RecursiveAI.OneContact;

public readonly partial struct PostalAddress
{
    private PostalAddress(AddressParts parts)
    {
        _parts = parts;
        _canonical = string.Join(", ", StructuralAddress.LabelLines(AddressDataSet.Embedded, parts, AddressScript.Auto));
        Source = default;
        Flags = AddressFacts.None;
    }

    /// <summary>Gets the country, or <see cref="Region.Unknown"/> when the address has none.</summary>
    public Region Country => _parts?.Country ?? default;

    /// <summary>Gets the BCP 47 language tag the address was written in (<c>fr</c>, <c>ja-Latn</c>), or <see langword="null"/> when none was given.</summary>
    public string? Language => _parts?.Language;

    /// <summary>Gets the administrative area (state, province, prefecture and so on); empty when absent.</summary>
    public string AdminArea => _parts?.AdminArea ?? string.Empty;

    /// <summary>Gets the locality (city or town); empty when absent.</summary>
    public string Locality => _parts?.Locality ?? string.Empty;

    /// <summary>Gets the dependent locality (district, suburb); empty when absent.</summary>
    public string DependentLocality => _parts?.DependentLocality ?? string.Empty;

    /// <summary>Gets the postal code as written; empty when absent.</summary>
    public string PostalCode => _parts?.PostalCode ?? string.Empty;

    /// <summary>Gets the sorting code; empty when absent.</summary>
    public string SortingCode => _parts?.SortingCode ?? string.Empty;

    /// <summary>Gets the organization; empty when absent.</summary>
    public string Organization => _parts?.Organization ?? string.Empty;

    /// <summary>Gets the recipient; empty when absent.</summary>
    public string Recipient => _parts?.Recipient ?? string.Empty;

    /// <summary>Gets the street address lines as written, in order; empty when there are none.</summary>
    public IReadOnlyList<string> StreetLines => _parts?.StreetLines ?? [];

    /// <summary>
    /// Gets how much the library knows about the country: <see cref="AddressCoverage.Full"/> for the supported tiers,
    /// <see cref="AddressCoverage.Structural"/> for every other country. The default value, without a country, is structural.
    /// </summary>
    public AddressCoverage Coverage => AddressCoverageTable.For(Country);

    /// <summary>Creates an address from its fields. Nothing is changed or validated: values are kept as written, and <see cref="Validate"/> reports problems.</summary>
    /// <param name="country">The country; <see cref="Region.Unknown"/> for none.</param>
    /// <param name="adminArea">The administrative area.</param>
    /// <param name="locality">The locality.</param>
    /// <param name="dependentLocality">The dependent locality.</param>
    /// <param name="postalCode">The postal code.</param>
    /// <param name="sortingCode">The sorting code.</param>
    /// <param name="organization">The organization.</param>
    /// <param name="recipient">The recipient.</param>
    /// <param name="streetLines">The street address lines, in order.</param>
    /// <param name="language">The BCP 47 language tag the address is written in; a tag naming the Latin script (<c>ja-Latn</c>) selects the country's Latin layout, and a tag the country has a data variant for (<c>fr</c> for Canada) selects that variant. The tag is kept in lower case with <c>-</c> separators. <see langword="null"/> for the country's default.</param>
    /// <returns>The address. Its canonical form is the single-line label in the country's layout.</returns>
    public static PostalAddress Create(
        Region country,
        string? adminArea = null,
        string? locality = null,
        string? dependentLocality = null,
        string? postalCode = null,
        string? sortingCode = null,
        string? organization = null,
        string? recipient = null,
        IEnumerable<string>? streetLines = null,
        string? language = null)
    {
        string[] lines = streetLines is null ? [] : [.. streetLines.Select(l => l ?? string.Empty)];
        return new PostalAddress(new AddressParts(
            country,
            NormalizeLanguage(language),
            adminArea ?? string.Empty,
            locality ?? string.Empty,
            dependentLocality ?? string.Empty,
            postalCode ?? string.Empty,
            sortingCode ?? string.Empty,
            organization ?? string.Empty,
            recipient ?? string.Empty,
            lines));
    }

    // BCP 47 tags are case-insensitive and the data names its language variants in lower case.
    private static string? NormalizeLanguage(string? language) =>
        string.IsNullOrWhiteSpace(language) ? null : language.Trim().Replace('_', '-').ToLowerInvariant();

    /// <summary>
    /// Checks the address against the structure of its country: required fields, fields the country does not use, subdivision
    /// names, and the postal code against the country's pattern and the prefix its state or province expects. It looks nothing up,
    /// so a well-formed address that does not exist passes. At most one issue is reported per field.
    /// </summary>
    /// <returns>The issues, in field order; empty when the structure is sound.</returns>
    public IReadOnlyList<AddressIssue> Validate() =>
        StructuralAddress.Validate(AddressDataSet.Embedded, _parts ?? new AddressParts(default, null, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, []));

    /// <summary>Lays the address out as a label in the country's format: one string per line, in the order the country writes them, with empty fields left out.</summary>
    /// <param name="script">Which layout to use where the country has a Latin and a local one.</param>
    /// <returns>The lines; empty for an address with no fields.</returns>
    public IReadOnlyList<string> GetLabelLines(AddressScript script = AddressScript.Auto) =>
        StructuralAddress.LabelLines(AddressDataSet.Embedded, _parts ?? new AddressParts(default, null, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, []), script);

    /// <summary>Lays the address out on one line: the label lines joined with a comma and a space.</summary>
    /// <param name="script">Which layout to use where the country has a Latin and a local one.</param>
    /// <returns>The single line.</returns>
    public string ToSingleLine(AddressScript script = AddressScript.Auto) => string.Join(", ", GetLabelLines(script));

    /// <summary>
    /// Parses free text as an address in a country. Free text is parsed only for countries in the supported tiers
    /// (<see cref="AddressCoverage.Full"/>); for any other country this refuses with <see cref="ReasonCode.AddressFreeTextRefused"/>.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="country">The country the text is in.</param>
    /// <param name="value">The address, or the default value when the status is not <see cref="OperationStatus.Done"/>.</param>
    /// <param name="detail">Issues with reason codes.</param>
    /// <returns><see cref="OperationStatus.Done"/>, or <see cref="OperationStatus.InvalidData"/> with the reason in <paramref name="detail"/>.</returns>
    public static OperationStatus TryParse(ReadOnlySpan<char> text, Region country, out PostalAddress value, out ParseDetail detail)
    {
        if (country.IsUnknown)
        {
            value = default;
            detail = ParseDetail.Failure(ReasonCode.AddressCountryRequired, new SourceSpan(0, text.Length));
            return OperationStatus.InvalidData;
        }

        if (AddressCoverageTable.For(country) != AddressCoverage.Full)
        {
            value = default;
            detail = ParseDetail.Failure(ReasonCode.AddressFreeTextRefused, new SourceSpan(0, text.Length));
            return OperationStatus.InvalidData;
        }

        return TryParse(text, out value, out detail);
    }
}
