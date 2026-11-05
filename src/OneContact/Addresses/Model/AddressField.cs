namespace RecursiveAI.OneContact.Addresses;

/// <summary>
/// The fields of a postal address, at the level of detail postal address metadata describes (country, administrative area,
/// locality, dependent locality, postal code, sorting code, organization, recipient and the street lines). Street parts such as
/// house number and street type sit underneath <see cref="StreetAddress"/>.
/// </summary>
public enum AddressField
{
    /// <summary>The country (a two-letter region code).</summary>
    Country = 0,

    /// <summary>The top-level subdivision: state, province, prefecture, island, county and so on. Its local name varies by country.</summary>
    AdminArea = 1,

    /// <summary>The city or town.</summary>
    Locality = 2,

    /// <summary>The part of a locality: a district, suburb, neighbourhood or village.</summary>
    DependentLocality = 3,

    /// <summary>The postal code (ZIP code, postcode, PIN and so on).</summary>
    PostalCode = 4,

    /// <summary>The sorting code (CEDEX in France, for example).</summary>
    SortingCode = 5,

    /// <summary>The organization (company or institution) the mail is for.</summary>
    Organization = 6,

    /// <summary>The person the mail is for.</summary>
    Recipient = 7,

    /// <summary>The street address lines.</summary>
    StreetAddress = 8,
}
