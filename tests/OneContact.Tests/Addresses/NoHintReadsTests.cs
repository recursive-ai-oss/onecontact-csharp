using System.Buffers;
using RecursiveAI.OneContact.Addresses.Parsing;

namespace RecursiveAI.OneContact.Tests.Addresses;

/// <summary>
/// Reads of text with no country hint: a country that cannot write the words of the text is not a candidate, a reading is only as good as its parts, and the forms of the
/// Isle of Man and Luxembourg that web pages use.
/// </summary>
public class NoHintReadsTests
{
    private static ParsedAddress Read(string text, string? hint = null)
    {
        OperationStatus status = RuleAddressParser.TryParse(text, hint is null ? Region.Unknown : Region.Parse(hint), out ParsedAddress? a, out ParseDetail d);
        Assert.True(status == OperationStatus.Done, $"not read: {d.PrimaryReason}");
        return a!;
    }

    private static string? Field(ParsedAddress a, string name) => a.Fields.FirstOrDefault(f => f.Name == name).Text;

    [Fact]
    public void ACzechOrSlovakPostcodeIsNotReadAsIcelandic()
    {
        // Iceland writes three digits; read that way "251 66 Hrusice" would leave "66 Hrusice" as the locality, which is the postcode's second half, not a place.
        ParsedAddress a = Read("Ladova 3/64, 251 66 Hrusice");
        Assert.True(a.Country.IsUnknown);
        Assert.Equal("251 66", a.PostalCode!.Value.Text);
        Assert.Equal("Hrusice", a.Locality!.Value.Text);
    }

    [Fact]
    public void ADutchStreetEndingLeavesOnlyCountriesThatWriteDutch()
    {
        // Belgium writes Dutch, French and German; Austria, Denmark, Hungary, Norway and Slovenia write none of Dutch, so they are not candidates, and the Netherlands does not write four plain digits.
        ParsedAddress a = Read("Kerkstraat 3, 1000 Brussel");
        Assert.Equal(Region.Parse("BE"), a.Country);
        Assert.True(a.CountryInferred);
    }

    [Fact]
    public void AFrenchStreetTypeLeavesCountriesThatWriteFrench()
    {
        // Belgium, Switzerland and Luxembourg write French and read the line alike; Hungary and Denmark, which would read "Rue du Fort" as a name, are not candidates.
        ParsedAddress a = Read("Rue du Fort 12, 4000 Liege");
        Assert.True(a.Country.IsUnknown);
        Assert.Equal("Rue du Fort", Field(a, "street.name"));
        Assert.Equal("12", Field(a, "street.house-number"));
    }

    [Fact]
    public void AllCapsGermanAlleeAndFrenchAlleeBothStayReadable()
    {
        // The words allee and chaussee belong to German, Dutch and French alike; they do not rule a French text out.
        ParsedAddress a = Read("12 Allee des Roses, 75001 Paris");
        // Paris is not evidence of France alone, so the country stays unknown; the fields are still read.
        Assert.True(a.Country.IsUnknown || a.Country == Region.Parse("FR"));
        Assert.Equal("des Roses", Field(a, "street.name"));
    }

    [Fact]
    public void ASpanishProvinceInParenthesesThatMatchesThePostcodeIsSpanishEvidence()
    {
        // UPU Spain sheet: the province's own two digits open the postcode. Mexico has no province Madrid, so the text is Spanish.
        ParsedAddress a = Read("Calle Mayor 5, 28013 Madrid (Madrid)");
        Assert.Equal(Region.Parse("ES"), a.Country);
        Assert.Equal("Madrid", a.AdminArea!.Value.Text);
    }

    [Fact]
    public void ASpanishStreetAloneIsSpanishOrMexican()
    {
        ParsedAddress a = Read("Plaza Mayor 1, 47001 Valladolid");
        Assert.True(a.Country.IsUnknown);
        Assert.True(a.Facts.HasFlag(AddressFacts.CountryUndetermined));
        Assert.Equal("Valladolid", a.Locality!.Value.Text);
    }

    [Fact]
    public void LuxembourgReadsATypeFirstStreetWithItsNumberLast()
    {
        // "Rue de la Loi 16" as Belgian and Swiss addresses write it; Luxembourg wrote the number into the street name.
        ParsedAddress a = Read("Rue de la Loi 16, 1000 Bruxelles", "LU");
        Assert.Equal("16", Field(a, "street.house-number"));
        Assert.Equal("Rue de la Loi", string.Join(' ', a.Fields.Where(f => f.Name is "street.type" or "street.name").Select(f => f.Text)));
        ParsedAddress first = Read("12 Rue Neuve, 1000 Luxembourg", "LU");
        Assert.Equal("12", Field(first, "street.house-number"));
    }

    [Theory]
    [InlineData("Victoria Road Douglas IM1 1AA", "Victoria Road", "Douglas", "")]
    [InlineData("Castle Street Castletown Isle of Man IM9 1LD", "Castle Street", "Castletown", "Isle of Man")]
    [InlineData("12 Athol Street Douglas Isle of Man IM1 1AA", "Athol Street", "Douglas", "Isle of Man")]
    [InlineData("Douglas Isle of Man IM1 2AB", "", "Douglas", "Isle of Man")]
    public void IsleOfManLinesWithoutCommasAreSplitAtTheRoadType(string text, string road, string town, string island)
    {
        // Royal Mail PAF Programmers' Guide: thoroughfare, then post town; Isle of Man Post prints the island's name after the post town.
        ParsedAddress a = Read(text, "IM");
        Assert.Equal(Region.Parse("IM"), a.Country);
        Assert.Equal(town, a.Locality!.Value.Text);
        Assert.Equal(road, string.Join(' ', a.Fields.Where(f => f.Name is "street.name" or "street.type").Select(f => f.Text)));
        Assert.Equal(island, a.CountryName?.Text ?? string.Empty);
        ParsedAddress inferred = Read(text);
        Assert.Equal(Region.Parse("IM"), inferred.Country);
    }
}
