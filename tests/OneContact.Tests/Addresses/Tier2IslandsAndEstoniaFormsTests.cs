using System.Buffers;
using RecursiveAI.OneContact.Addresses;
using RecursiveAI.OneContact.Addresses.Parsing;

namespace RecursiveAI.OneContact.Tests.Addresses;

/// <summary>
/// Web forms of Manx, Irish, British, Jersey and Estonian addresses (all inputs written for this repository): the island's abbreviation or "(UK)" beside the postcode,
/// "Channel Islands" after the island, the country between a county and the postcode, a Jersey parish after a house name and a road on one line, an Irish last line with a
/// county marker and no postcode, and the Estonian county on a line after the town or between the town and the postcode (UPU Estonia sheet, Omniva).
/// </summary>
public class Tier2IslandsAndEstoniaFormsTests
{
    private static ParsedAddress Read(string text, string? hint = null)
    {
        OperationStatus status = RuleAddressParser.TryParse(text, hint is null ? Region.Unknown : Region.Parse(hint), out ParsedAddress? a, out ParseDetail d);
        Assert.True(status == OperationStatus.Done, $"{text}: {d.PrimaryReason}");
        foreach (ParsedAddressField f in a!.Fields)
        {
            Assert.Equal(f.Text, text.Substring(f.Span.Start, f.Span.Length));
        }

        return a;
    }

    private static string? Field(ParsedAddress a, string name) => a.Fields.Where(f => f.Name == name).Select(f => f.Text).FirstOrDefault();

    [Theory]
    [InlineData("12 Athol Street, Douglas, IM1 1AA, IOM", "Douglas", "IOM")]
    [InlineData("12 Athol Street, Douglas, IM1 1AA, IoM", "Douglas", "IoM")]
    [InlineData("12 Athol Street, Douglas, IM1 1AA (UK)", "Douglas", "UK")]
    [InlineData("12 Athol Street, Douglas, Isle of Man (UK) IM1 1AA", "Douglas", "Isle of Man (UK)")]
    [InlineData("Peel Road, Douglas, British Isles IM1 5PZ", "Douglas", "British Isles")]
    public void ManxCountryText(string input, string locality, string country)
    {
        ParsedAddress a = Read(input);
        Assert.Equal(Region.Parse("IM"), a.Country);
        Assert.Equal(locality, Field(a, "locality"));
        Assert.Equal(country, Field(a, "country"));
    }

    [Fact]
    public void ChannelIslandsAfterTheIslandNamesTheIsland()
    {
        ParsedAddress a = Read("Bagot Road, St Helier, Jersey, Channel Islands JE2 4AB");
        Assert.Equal(Region.Parse("JE"), a.Country);
        Assert.Equal("St Helier", Field(a, "locality"));
    }

    [Fact]
    public void CountyThenCountryThenPostcodeOnTheirOwnUnits()
    {
        ParsedAddress a = Read("High Street, Guildford, Surrey, England, GU1 3AA");
        Assert.Equal("Guildford", Field(a, "locality"));
        Assert.Equal("Surrey", Field(a, "admin-area"));
        Assert.Equal("England", Field(a, "country"));
    }

    [Fact]
    public void JerseyHouseNameRoadAndParishOnOneLineAboveTheIsland()
    {
        ParsedAddress a = Read("Mon Plaisir, La Rue du Hocq, St Clement\nJersey JE2 6LF", "JE");
        Assert.Equal(Region.Parse("JE"), a.Country);
        Assert.Equal("St Clement", Field(a, "locality"));
        Assert.Equal("Jersey", Field(a, "country"));
        Assert.Equal("Mon Plaisir, La Rue du Hocq", Field(a, "leading-line"));
    }

    [Theory]
    [InlineData("Patrick Street, Cork, Co. Cork", "Cork", "Co. Cork")]
    [InlineData("Main Street, Naas, Co. Kildare", "Naas", "Co. Kildare")]
    public void IrishLastLineWithACountyMarkerNeedsNoHint(string input, string locality, string county)
    {
        ParsedAddress a = Read(input);
        Assert.Equal(Region.Parse("IE"), a.Country);
        Assert.Equal(locality, Field(a, "locality"));
        Assert.Equal(county, Field(a, "admin-area"));
    }

    [Fact]
    public void DublinDistrictNeedsNoHint() => Assert.Equal(Region.Parse("IE"), Read("Dame Street, Dublin 2").Country);

    [Theory]
    [InlineData("Patrick Street, Cork")]
    [InlineData("Main Street, Springfield")]
    public void ABareTownIsNotIrishEvidence(string input) =>
        Assert.NotEqual(OperationStatus.Done, RuleAddressParser.TryParse(input, Region.Unknown, out _, out _));

    [Theory]
    [InlineData("Pärnu mnt 12, 10148 Tallinn, Harju maakond", "Tallinn", "Harju maakond", "10148")]
    [InlineData("Pärnu mnt 12, Tallinn 10148, Harju maakond", "Tallinn", "Harju maakond", "10148")]
    [InlineData("Pärnu mnt 12, Tallinn, Harju maakond 10148", "Tallinn", "Harju maakond", "10148")]
    [InlineData("Ülikooli 18, Tartu, Tartu maakond, 51003", "Tartu", "Tartu maakond", "51003")]
    [InlineData("Lai tn 5, 80010 Pärnu, Pärnu maakond", "Pärnu", "Pärnu maakond", "80010")]
    public void EstonianCountyBesideTheTown(string input, string locality, string county, string postcode)
    {
        ParsedAddress a = Read(input);
        Assert.Equal(Region.Parse("EE"), a.Country);
        Assert.Equal(locality, Field(a, "locality"));
        Assert.Equal(county, Field(a, "admin-area"));
        Assert.Equal(postcode, Field(a, "postal-code"));
    }

    [Fact]
    public void EstonianVillageCountyFormIsUnchanged()
    {
        ParsedAddress a = Read("Kooli 3, Kodu küla, Rae vald, 75301 Harju maakond");
        Assert.Equal("Kodu küla", Field(a, "locality"));
        Assert.Equal("Harju maakond", Field(a, "admin-area"));
    }
}
