using System.Buffers;
using RecursiveAI.OneContact.Addresses;
using RecursiveAI.OneContact.Addresses.Expansion;
using RecursiveAI.OneContact.Addresses.Parsing;
using RecursiveAI.OneContact.Addresses.Standardization;
using RecursiveAI.OneContact.Addresses.Streets;
using RecursiveAI.OneContact.Keys;

namespace RecursiveAI.OneContact.Tests.Addresses;

/// <summary>
/// Every example address of the free guides of the second Tier 2 group (the UPU country sheets for the United Kingdom, Jersey, Guernsey, the Isle of Man, Ireland, Spain,
/// Portugal, Italy, San Marino, the Vatican, Malta and Andorra), as transcribed in testdata/addresses/authority-examples-tier2b.tsv with its citation, and the rules those sheets state.
/// </summary>
public class Tier2GroupBTests
{
    public static TheoryData<string, string, string, string, string, string, string> Cases()
    {
        TheoryData<string, string, string, string, string, string, string> data = [];
        foreach (string[] c in AddressTestData.Rows("testdata/addresses/authority-examples-tier2b.tsv", 7))
        {
            data.Add(c[0], c[1], c[2], c[3], c[4].Replace("\\n", "\n", StringComparison.Ordinal), c[5].Replace("\\n", "\n", StringComparison.Ordinal), c[6].Replace("\\n", "\n", StringComparison.Ordinal));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ExampleIsStandardized(string id, string citation, string hint, string withCountry, string input, string postal, string display)
    {
        Region region = hint.Length == 0 ? Region.Unknown : Region.Parse(hint);
        OperationStatus status = RuleAddressParser.TryParse(input, region, out ParsedAddress? address, out ParseDetail detail);
        Assert.True(status == OperationStatus.Done, $"{id} ({citation}) was not read: {detail.PrimaryReason}");
        bool country = withCountry == "1";
        Assert.Equal(postal, address!.ToString(AddressStyle.Postal, AddressLayout.Label, country));
        if (display != "=")
        {
            Assert.Equal(display, address.ToString(AddressStyle.Display, AddressLayout.Label, country));
        }

        Assert.Equal(region, address.Country);
        foreach (ParsedAddressField f in address.Fields)
        {
            Assert.Equal(f.Text, input.Substring(f.Span.Start, f.Span.Length));
        }
    }

    private static ParsedAddress Parse(string text, string? hint, out OperationStatus status)
    {
        status = RuleAddressParser.TryParse(text, hint is null ? Region.Unknown : Region.Parse(hint), out ParsedAddress? a, out ParseDetail d);
        Assert.True(status == OperationStatus.Done, $"not read: {d.PrimaryReason}");
        return a!;
    }

    private static string Key(string text, string hint)
    {
        Assert.True(PostalAddress.TryParse(text, Region.Parse(hint), out PostalAddress p, out ParseDetail d) == OperationStatus.Done, $"{text}: {d.PrimaryReason}");
        return AddressKey.Compute(p, KeyVersion.V1);
    }

    [Theory]
    [InlineData("SW1A 1AA", "GB")]
    [InlineData("GU1 4AB", "GB")]
    [InlineData("JE2 3AB", "JE")]
    [InlineData("GY1 1AA", "GG")]
    [InlineData("IM1 2AB", "IM")]
    [InlineData("BT1 5GS", "GB")]
    public void PostcodeAreaDecidesBetweenBritainAndTheCrownDependencies(string postcode, string country)
    {
        // UPU United Kingdom sheet, list of postcode areas: JE, GY and IM belong to Jersey, Guernsey and the Isle of Man.
        Assert.Equal(Region.Parse(country), Parse("1 High Street\nTOWN\n" + postcode, null, out _).Country);
    }

    [Fact]
    public void SingleLineBritishAddressIsReadAndOffsetsPointIntoTheInput()
    {
        const string text = "Flat 2, 10 Downing Street, London SW1A 2AA, United Kingdom";
        ParsedAddress a = Parse(text, null, out _);
        Assert.Equal(Region.Parse("GB"), a.Country);
        Assert.Equal("Flat 2\n10 Downing Street\nLONDON\nSW1A 2AA", a.ToString(AddressStyle.Postal));
        foreach (ParsedAddressField f in a.Fields)
        {
            Assert.Equal(f.Text, text.Substring(f.Span.Start, f.Span.Length));
        }

        Assert.Equal("London", a.Locality!.Value.Text);
    }

    [Fact]
    public void BritishPostcodeIsWrittenWithOneSpaceInCapitals()
    {
        Assert.Equal("EC1Y 8SY", Parse("49 Featherstone Street\nLondon\nec1y 8sy", "GB", out _).PostalCode!.Value.Standard);
        Assert.Equal("GIR 0AA", Parse("1 Bank Street\nCARDIFF\nGIR 0AA", "GB", out _).PostalCode!.Value.Standard);
    }

    [Fact]
    public void PostcodeWithoutAPostTownIsNotGuessed()
    {
        // The line above the postcode is a street: there is no post town to take, and none is invented.
        Assert.Equal(OperationStatus.InvalidData, RuleAddressParser.TryParse("10 Downing Street\nSW1A 2AA", Region.Parse("GB"), out _, out _));
    }

    [Fact]
    public void IrishCountyIsTheAdminAreaAndEircodeMayBeGlued()
    {
        ParsedAddress a = Parse("Ms M Sullivan, 56 Broomfield, Macroom, Co. Cork, T37F8HK", "IE", out _);
        Assert.Equal("MACROOM", a.Locality!.Value.Standard);
        Assert.Equal("CO. CORK", a.AdminArea!.Value.Standard);
        Assert.Equal("T37 F8HK", a.PostalCode!.Value.Standard);
        Assert.Equal("Ms M Sullivan\n56 BROOMFIELD\nMACROOM\nCO. CORK\nT37 F8HK", a.ToString(AddressStyle.Postal));
    }

    [Fact]
    public void SpanishFloorAndDoorKeepTheirCommasAndTheProvinceIsAdminArea()
    {
        // UPU Spain sheet: information about staircase, floor and door is separated from the street number by a comma; the province is on a line of its own.
        ParsedAddress a = Parse("Calle Huertas 18, 4º, C, 29400 Marbella, Málaga, España", null, out _);
        Assert.Equal(Region.Parse("ES"), a.Country);
        Assert.Equal("Calle Huertas 18, 4º, C\n29400 MARBELLA\nMÁLAGA", a.ToString(AddressStyle.Postal));
        Assert.Equal("MÁLAGA", a.AdminArea!.Value.Standard);
        Assert.Equal("Calle Huertas 18, 4º, C, 29400 MARBELLA, MÁLAGA", a.ToString(AddressStyle.Postal, AddressLayout.SingleLine));
    }

    [Fact]
    public void SpanishProvinceInParenthesesIsRead()
    {
        ParsedAddress a = Parse("Calle Mayor, 5\n28013 Madrid (Madrid)", "ES", out _);
        Assert.Equal("Calle Mayor 5\n28013 MADRID\nMADRID", a.ToString(AddressStyle.Postal));
    }

    [Fact]
    public void SpanishStreetNumberAfterANumberedNameIsTheLastNumber()
    {
        Assert.Equal("Calle 5 de Mayo 12", Parse("Calle 5 de Mayo 12\n28013 Madrid\nMadrid", "ES", out _).ToString(AddressStyle.Postal).Split('\n')[0]);
        ParsedAddress named = Parse("Rua 25 de Abril\n1000-001 Lisboa", "PT", out _);
        Assert.Equal("RUA 25 DE ABRIL", named.ToString(AddressStyle.Postal).Split('\n')[0]);
        Assert.DoesNotContain(named.DeliveryLines[0].Parts, p => p.Role == StreetPartRole.HouseNumber);
    }

    [Fact]
    public void FiveDigitsAndStreetWordsAloneDoNotMakeAnAddressSpanish()
    {
        // American addresses use Plaza and Camino too; without a province line, a country name or a hint the rules do not read the address as Spanish.
        OperationStatus s = RuleAddressParser.TryParse("100 Main Plaza\nAustin 78701", Region.Unknown, out ParsedAddress? read, out _);
        Assert.True(s == OperationStatus.InvalidData || read!.Country == Region.US);
        Assert.Equal(Region.US, Parse("100 Main Plaza\nAustin TX 78701", null, out _).Country);
        Assert.Equal(Region.Parse("ES"), Parse("Calle Mayor 5\n28013 Madrid\nMadrid", null, out _).Country);
    }

    [Fact]
    public void ItalianProvinceCodeIsAdminAreaAndIsEvidenceForItaly()
    {
        ParsedAddress a = Parse("Viale Europa 22, 00122 Roma RM", null, out _);
        Assert.Equal(Region.Parse("IT"), a.Country);
        Assert.True(a.CountryInferred);
        Assert.Equal("RM", a.AdminArea!.Value.Standard);
        Assert.Equal("VIALE EUROPA 22\n00122 ROMA RM", a.ToString(AddressStyle.Postal));

        // A country code that is also a province code (FR is Frosinone) is not read as a province.
        Assert.Equal(Region.Parse("FR"), Parse("25 rue des Fleurs\n33500 Libourne FR", "FR", out _).Country);
    }

    [Fact]
    public void ItalianLinesLoseTheirPunctuation()
    {
        // UPU Italy sheet: avoid punctuation from the street line on; capitals.
        Assert.Equal("VIA ROMA 12\n00100 ROMA RM", Parse("Via Roma, 12\n00100 Roma RM", "IT", out _).ToString(AddressStyle.Postal));
    }

    [Fact]
    public void PortuguesePostcodeAndCapitals()
    {
        ParsedAddress a = Parse("Rua das Flores, 14 - 3º Esq., 4050-262 Porto", null, out _);
        Assert.Equal(Region.Parse("PT"), a.Country);
        Assert.Equal("4050-262", a.PostalCode!.Value.Standard);
        Assert.EndsWith("4050-262 PORTO", a.ToString(AddressStyle.Postal), StringComparison.Ordinal);
    }

    [Fact]
    public void SanMarinoAndVaticanPostcodesAreTheirOwn()
    {
        Assert.Equal(Region.Parse("SM"), Parse("Contrada Omerelli 17\n47890 San Marino", null, out _).Country);
        Assert.Equal(Region.Parse("VA"), Parse("Piazza San Pietro\n00120 Città del Vaticano", null, out _).Country);
    }

    [Fact]
    public void MalteseAndAndorranPostcodesAreRead()
    {
        ParsedAddress mt = Parse("38 Triq it-Tempji Neolitici, Il-Hamrun HMR 1428, Malta", null, out _);
        Assert.Equal(Region.Parse("MT"), mt.Country);
        Assert.Equal("IL-HAMRUN", mt.Locality!.Value.Standard);
        Assert.Equal("HMR 1428", mt.PostalCode!.Value.Standard);
        ParsedAddress ad = Parse("Carrer Aigueta 19, AD500 Andorra la Vella", null, out _);
        Assert.Equal(Region.Parse("AD"), ad.Country);
        Assert.Equal("AD500", ad.PostalCode!.Value.Standard);
    }

    [Fact]
    public void ChannelIslandsLineNeedsAPostcodeAreaOfJerseyOrGuernsey()
    {
        Assert.Equal(Region.Parse("JE"), Parse("1 Rue Neuve\nSt Helier\nJE2 3AB\nChannel Islands", null, out _).Country);
        Assert.Equal(OperationStatus.InvalidData, RuleAddressParser.TryParse("1 High Street\nLondon\nSW1A 1AA\nChannel Islands", Region.Unknown, out _, out _));
    }

    [Fact]
    public void KeysAgreeAcrossAbbreviationsAndSpacing()
    {
        Assert.Equal(Key("10 Downing St\nLondon\nSW1A 2AA", "GB"), Key("10 Downing Street\nLONDON\nSW1A 2AA", "GB"));
        Assert.Equal(Key("Calle Mayor 5\n28013 Madrid\nMadrid", "ES"), Key("C/ Mayor, 5\n28013 MADRID (Madrid)", "ES"));
        Assert.NotEqual(Key("Calle Mayor 5\n28013 Madrid\nMadrid", "ES"), Key("Calle Mayor 6\n28013 Madrid\nMadrid", "ES"));
        Assert.StartsWith("gb|", Key("10 Downing St\nLondon\nSW1A 2AA", "GB"), StringComparison.Ordinal);
    }

    [Fact]
    public void ExpansionKnowsTheNewLanguages()
    {
        Assert.Contains("calle mayor 5", AddressExpander.Expand("Cl Mayor 5", ExpansionLanguages.Spanish), StringComparer.Ordinal);
        Assert.Contains("rua das flores 14", AddressExpander.Expand("R. das Flores 14", ExpansionLanguages.Portuguese), StringComparer.Ordinal);
    }

    [Fact]
    public void GroupAAndUsCaAreReadAsBefore()
    {
        Assert.Equal(Region.Parse("FR"), Parse("25 rue des Fleurs\n33500 Libourne", null, out _).Country);
        Assert.Equal(Region.Parse("DE"), Parse("Wacholderweg 52a\n26133 Oldenburg", null, out _).Country);
        Assert.Equal(Region.US, Parse("123 Main St\nSpringfield VA 22162", null, out _).Country);
    }
}
