using System.Buffers;
using RecursiveAI.OneContact.Addresses;
using RecursiveAI.OneContact.Addresses.Parsing;

namespace RecursiveAI.OneContact.Tests.Addresses;

/// <summary>
/// Orders of the last group that web pages use (all inputs written for this repository): the postcode in front of its locality and the street after them, the locality
/// before the street with the postcode last, the postcode set apart behind its locality, a country name between the post town and a British postcode, an Australian
/// state written in full, and a Spanish street known by its name alone. The posts' sheets print their own order; the reading is the same fields.
/// </summary>
public class Tier2LastLineOrderTests
{
    private static ParsedAddress Read(string text, string? hint)
    {
        OperationStatus status = RuleAddressParser.TryParse(text, hint is null ? Region.Unknown : Region.Parse(hint), out ParsedAddress? a, out ParseDetail d);
        Assert.True(status == OperationStatus.Done, $"{text}: {d.PrimaryReason}");
        foreach (ParsedAddressField f in a!.Fields)
        {
            Assert.Equal(f.Text, text.Substring(f.Span.Start, f.Span.Length));
        }

        return a;
    }

    private static string Fields(ParsedAddress a) => string.Join(" ; ", a.Fields.Select(f => f.Name + "=" + f.Text));

    [Theory]
    [InlineData("PL", "00-590 Warszawa, ul. Marszałkowska 10", "street.type=ul. ; street.name=Marszałkowska ; street.house-number=10 ; locality=Warszawa ; postal-code=00-590")]
    [InlineData("PL", "Warszawa, ul. Złota 59, 00-120", "street.type=ul. ; street.name=Złota ; street.house-number=59 ; locality=Warszawa ; postal-code=00-120")]
    [InlineData("PL", "ul. Kościuszki 12, Gdynia 81-001", "street.name=Kościuszki ; street.house-number=12 ; locality=Gdynia ; postal-code=81-001")]
    [InlineData("PL", "Kraków 31-147, ul. Długa 5", "street.name=Długa ; street.house-number=5 ; locality=Kraków ; postal-code=31-147")]
    [InlineData("ES", "28013 Madrid, Calle Mayor 5", "street.type=Calle ; street.name=Mayor ; street.house-number=5 ; locality=Madrid ; postal-code=28013")]
    [InlineData("ES", "08037 Barcelona, Avinguda Diagonal 400", "street.type=Avinguda ; street.name=Diagonal ; street.house-number=400 ; locality=Barcelona ; postal-code=08037")]
    [InlineData("HU", "Fő utca 12., Debrecen, 4024", "street.name=Fő ; street.type=utca ; street.house-number=12. ; locality=Debrecen ; postal-code=4024")]
    public void PostcodeAndLocalityOrders(string hint, string input, string expected)
    {
        string fields = Fields(Read(input, hint));
        foreach (string part in expected.Split(" ; "))
        {
            Assert.Contains(part.TrimEnd(' '), fields, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CountryNameBetweenPostTownAndPostcodeIsTheCountry()
    {
        ParsedAddress a = Read("12 High Street, Belfast, UK BT1 1GH", "GB");
        Assert.Equal("Belfast", a.Locality!.Value.Text);
        Assert.Equal("BT1 1GH", a.PostalCode!.Value.Text);
        Assert.Equal("UK", a.CountryName!.Value.Text);

        ParsedAddress b = Read("The Old Mill, Mill Lane, Guildford, Surrey, England GU1 3AB", "GB");
        Assert.Equal("Guildford", b.Locality!.Value.Text);
        Assert.Equal("Surrey", b.AdminArea!.Value.Text);
        Assert.Equal("England", b.CountryName!.Value.Text);
    }

    // Scottish council areas written "Dundee City" are counties of the line above the postcode, not the post town.
    [Fact]
    public void CouncilAreaIsTheCountyNotThePostTown()
    {
        ParsedAddress a = Read("34 Findcastle Place, Dundee, Dundee City, DD4 9EN", "GB");
        Assert.Equal("Dundee", a.Locality!.Value.Text);
        Assert.Equal("Dundee City", a.AdminArea!.Value.Text);
    }

    // A city that is also a county, written with the postcode on its line, is the post town; the business park above it is a line of the address (Royal Mail PAF Programmers' Guide: building and estate names precede the post town).
    [Fact]
    public void CityNamedLikeACountyIsThePostTownBehindABusinessPark()
    {
        ParsedAddress a = Read("Unit 3, Westside Business Park, Galway H91 X2Y3", "IE");
        Assert.Equal("Galway", a.Locality!.Value.Text);
        Assert.Equal("H91 X2Y3", a.PostalCode!.Value.Text);
        Assert.Null(a.AdminArea);
    }

    [Fact]
    public void AustralianStateInFullWithCountryName()
    {
        ParsedAddress a = Read("12 Queen St, Melbourne, Victoria 3000, Australia", "AU");
        Assert.Equal("Melbourne", a.Locality!.Value.Text);
        Assert.Equal("Victoria", a.AdminArea!.Value.Text);
        Assert.Equal("3000", a.PostalCode!.Value.Text);
        Assert.Equal("Australia", a.CountryName!.Value.Text);
    }

    [Theory]
    [InlineData("Gran Vía 25, 28013 Madrid", "Gran Vía")]
    [InlineData("Mayor 5, 28013 Madrid", "Mayor")]
    public void SpanishStreetKnownByItsNameAlone(string input, string name)
    {
        ParsedAddress a = Read(input, "ES");
        Assert.Contains("street.name=" + name, Fields(a), StringComparison.Ordinal);
        Assert.Equal("Madrid", a.Locality!.Value.Text);
    }

    // A postcode form that several countries share is never read without a country: the same digits would be a postcode in each of them.
    [Fact]
    public void SharedPostcodeFormWithoutCountryStaysRefused()
    {
        OperationStatus status = RuleAddressParser.TryParse("28013 Madrid, Mayor 5", Region.Unknown, out _, out ParseDetail d);
        Assert.NotEqual(OperationStatus.Done, status);
        Assert.NotNull(d);
    }
}
