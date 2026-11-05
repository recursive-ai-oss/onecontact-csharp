namespace RecursiveAI.OneContact.Addresses;

/// <summary>
/// The fields of an address as plain strings plus the country and language they were written in. Immutable. Absent values are
/// empty strings, never <see langword="null"/>.
/// </summary>
internal sealed class AddressParts
{
    internal AddressParts(
        Region country,
        string? language,
        string adminArea,
        string locality,
        string dependentLocality,
        string postalCode,
        string sortingCode,
        string organization,
        string recipient,
        IReadOnlyList<string> streetLines)
    {
        Country = country;
        Language = string.IsNullOrWhiteSpace(language) ? null : language.Trim();
        AdminArea = adminArea;
        Locality = locality;
        DependentLocality = dependentLocality;
        PostalCode = postalCode;
        SortingCode = sortingCode;
        Organization = organization;
        Recipient = recipient;
        StreetLines = streetLines;
    }

    /// <summary>Gets the country.</summary>
    internal Region Country { get; }

    /// <summary>Gets the BCP 47 language tag the address is written in, or <see langword="null"/> when none was given.</summary>
    internal string? Language { get; }

    /// <summary>Gets the administrative area.</summary>
    internal string AdminArea { get; }

    /// <summary>Gets the locality.</summary>
    internal string Locality { get; }

    /// <summary>Gets the dependent locality.</summary>
    internal string DependentLocality { get; }

    /// <summary>Gets the postal code.</summary>
    internal string PostalCode { get; }

    /// <summary>Gets the sorting code.</summary>
    internal string SortingCode { get; }

    /// <summary>Gets the organization.</summary>
    internal string Organization { get; }

    /// <summary>Gets the recipient.</summary>
    internal string Recipient { get; }

    /// <summary>Gets the street address lines.</summary>
    internal IReadOnlyList<string> StreetLines { get; }

    /// <summary>Gets a single-valued field. The street lines come back joined with a line feed.</summary>
    /// <param name="field">The field. <see cref="AddressField.Country"/> gives the region code.</param>
    /// <returns>The value, possibly empty.</returns>
    internal string Get(AddressField field) => field switch
    {
        AddressField.Country => Country.IsUnknown ? string.Empty : Country.Code,
        AddressField.AdminArea => AdminArea,
        AddressField.Locality => Locality,
        AddressField.DependentLocality => DependentLocality,
        AddressField.PostalCode => PostalCode,
        AddressField.SortingCode => SortingCode,
        AddressField.Organization => Organization,
        AddressField.Recipient => Recipient,
        AddressField.StreetAddress => string.Join('\n', StreetLines),
        _ => string.Empty,
    };
}
