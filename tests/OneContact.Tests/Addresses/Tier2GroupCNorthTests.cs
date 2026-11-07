using System.Buffers;
using RecursiveAI.OneContact.Addresses;
using RecursiveAI.OneContact.Addresses.Expansion;
using RecursiveAI.OneContact.Addresses.Parsing;
using RecursiveAI.OneContact.Addresses.Standardization;
using RecursiveAI.OneContact.Addresses.Streets;

namespace RecursiveAI.OneContact.Tests.Addresses;

/// <summary>
/// Every example address of the free guidance of the postal authorities of Denmark, Sweden, Finland, Norway, Iceland, Estonia, Latvia and Lithuania, as transcribed in
/// testdata/addresses/authority-examples-tier2c-north.tsv with its citation, and the rules those sources state.
/// </summary>
public class Tier2GroupCNorthTests
{
    private const string Examples = "testdata/addresses/authority-examples-tier2c-north.tsv";

    public static TheoryData<string, string, string, string, string, string, string> Cases()
    {
        TheoryData<string, string, string, string, string, string, string> data = [];
        foreach (string[] c in AddressTestData.Rows(Examples, 7))
        {
            data.Add(c[0], c[1], c[2], c[3], c[4].Replace("\\n", "\n", StringComparison.Ordinal), c[5].Replace("\\n", "\n", StringComparison.Ordinal), c[6].Replace("\\n", "\n", StringComparison.Ordinal));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ExampleIsStandardized(string id, string citation, string hint, string withCountry, string input, string postal, string display)
    {
        Region region = Region.Parse(hint);
        OperationStatus status = RuleAddressParser.TryParse(input, region, out ParsedAddress? address, out ParseDetail detail);
        Assert.True(status == OperationStatus.Done, $"{id} ({citation}) was not read: {detail.PrimaryReason}");
        bool country = withCountry == "1";
        Assert.Equal(postal, address!.ToString(AddressStyle.Postal, AddressLayout.Label, country));
        if (display != "=")
        {
            Assert.Equal(display, address.ToString(AddressStyle.Display, AddressLayout.Label, country));
        }

        Assert.Equal(region, address.Country);
        Assert.DoesNotContain(detail.Issues, i => i.Severity == IssueSeverity.Warning);
        foreach (ParsedAddressField f in address.Fields)
        {
            Assert.Equal(f.Text, input.Substring(f.Span.Start, f.Span.Length));
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ExampleThatPrintsTheCountryIsReadWithoutAHint(string id, string citation, string hint, string withCountry, string input, string postal, string display)
    {
        // The country name (or the LV-/LT- prefix) is evidence enough: no hint is needed, and the text is not taken for a United States or Canadian address.
        if (withCountry != "1")
        {
            return;
        }

        _ = display;
        OperationStatus status = RuleAddressParser.TryParse(input, Region.Unknown, out ParsedAddress? address, out ParseDetail detail);
        Assert.True(status == OperationStatus.Done, $"{id} ({citation}) was not read without a hint: {detail.PrimaryReason}");
        Assert.Equal(Region.Parse(hint), address!.Country);
        Assert.Equal(postal, address.ToString(AddressStyle.Postal, AddressLayout.Label, true));
    }

    private static ParsedAddress Parse(string text, string? hint)
    {
        OperationStatus status = RuleAddressParser.TryParse(text, hint is null ? Region.Unknown : Region.Parse(hint), out ParsedAddress? a, out ParseDetail d);
        Assert.True(status == OperationStatus.Done, $"not read: {d.PrimaryReason}");
        return a!;
    }

    [Theory]
    [InlineData("Herr H. C. Andersen\nEventyrvejen 1\nDK-5000 Odense\nDANMARK", "DK", "5000", "ODENSE")]
    [InlineData("Hr. Jón Jónsson\nEinimel 80\nIS-107 Reykjavik\nISLAND", "IS", "107", "REYKJAVIK")]
    [InlineData("Fru Inger Lilja\nVasavägen 3\nSE-582 20 Linköping\nSVERIGE", "SE", "582 20", "LINKÖPING")]
    public void ForeignAddressesOfPostenNorgeAreReadByTheirOwnCountryRules(string text, string country, string postcode, string locality)
    {
        // Posten Norge's page on mail to other countries: the country code, a dash and the postcode, and the country name in Norwegian.
        ParsedAddress a = Parse(text, null);
        Assert.Equal(Region.Parse(country), a.Country);
        Assert.Equal(postcode, a.PostalCode!.Value.Standard);
        Assert.Equal(locality, a.Locality!.Value.Standard);
    }

    [Theory]
    [InlineData("Kattstigen 5\n12345 Täby", "SE", "123 45")]
    [InlineData("Kattstigen 5\n123 45 Täby", "SE", "123 45")]
    [InlineData("Hiomotie 10a\n00120 Helsinki", "FI", "00120")]
    [InlineData("Narva mnt 7-12\n10117 Tallinn", "EE", "10117")]
    [InlineData("Gedimino pr. 9-12\n01103 Vilnius", "LT", "01103")]
    [InlineData("Storgata 15\n0161 Oslo", "NO", "0161")]
    [InlineData("Vesterbrogade 12, 3. th\n1620 København V", "DK", "1620")]
    [InlineData("Tryggvagötu 5\n101 Reykjavík", "IS", "101")]
    public void SharedPostcodeFormsAreReadWhenTheHintNamesTheCountry(string text, string country, string postcode)
    {
        // Five digits belong to six countries, four to seven and three to a house number anywhere but Iceland: the hint decides, never the digits alone.
        ParsedAddress a = Parse(text, country);
        Assert.Equal(Region.Parse(country), a.Country);
        Assert.Equal(postcode, a.PostalCode!.Value.Standard);
    }

    [Theory]
    [InlineData("Kase 7\n76001 Aegviidu")]
    [InlineData("Skogen 15\n0161 Oslo")]
    [InlineData("Hiomo 10a\n00380 Helsinki")]
    public void SharedPostcodeFormsWithoutAnyEvidenceAreNotGuessed(string text)
    {
        // No street ending, abbreviation or letter of the text belongs to one country; with one (Narva mnt, Storgata, Hiomotie, or a town of Estonia such as Tallinn) the country is inferred (Tier2NordicBalticFormsTests).
        Assert.True(AddressTestData.NamesNoCountry(text), text);
    }

    [Fact]
    public void FinnishPostcode00120IsHelsinkiWhenFinlandIsNamedAndTheVaticanOtherwise()
    {
        Assert.Equal(Region.Parse("FI"), Parse("Mannerheimintie 1\n00120 Helsinki\nFinland", null).Country);
        Assert.Equal(Region.Parse("VA"), Parse("Via della Conciliazione 1\n00120 Città del Vaticano", "VA").Country);
    }

    [Fact]
    public void DanishFloorDoorAndDistrictStayOnTheStreetLineWhenWrittenOnOneLine()
    {
        // Danmarks Adresser: street name, number, a comma, floor, a period, the side, a comma, the supplementary town name.
        const string text = "Hr. Henrik Nielsen, Kastanievej 15, 2, Agerskov, 8660 Skanderborg";
        ParsedAddress a = Parse(text, "DK");
        Assert.Equal("Hr. Henrik Nielsen\nKastanievej 15, 2\nAgerskov\n8660 SKANDERBORG", a.ToString(AddressStyle.Postal));
        Assert.Contains(a.DeliveryLines[0].Parts, p => p.Role == StreetPartRole.HouseNumber && p.Text == "15");
        Assert.Contains(a.DeliveryLines[0].Parts, p => p.Role == StreetPartRole.UnitNumber && p.Text == "2");
        foreach (ParsedAddressField f in a.Fields)
        {
            Assert.Equal(f.Text, text.Substring(f.Span.Start, f.Span.Length));
        }
    }

    [Theory]
    [InlineData("Brīvības ielā 90—23", "90", "23")]
    [InlineData("Salnas ielā 48/50", "48/50", "")]
    [InlineData("Raunas ielā 45/4—22", "45/4", "22")]
    [InlineData("Raunas 45/4—22", "45/4", "22")]
    public void LatvianHouseAndApartmentNumbersAreRead(string street, string house, string apartment)
    {
        // The Latvian rules for a unified postal address: a dash between the house and apartment number, a slash for a double number or a building.
        ParsedAddress a = Parse(street + "\nRīga, LV-1010", "LV");
        Assert.Equal(house, a.DeliveryLines[0].Parts.Single(p => p.Role == StreetPartRole.HouseNumber).Text);
        Assert.Equal(apartment, a.DeliveryLines[0].Parts.SingleOrDefault(p => p.Role == StreetPartRole.UnitNumber).Text ?? string.Empty);
        Assert.Equal(street, a.ToString(AddressStyle.Postal).Split('\n')[0]);
        Assert.Equal("RīGA, LV-1010".ToUpperInvariant(), a.ToString(AddressStyle.Postal).Split('\n')[1]);
    }

    [Fact]
    public void LatvianPostcodeAfterTheLocalityIsReadOnOneLineAndOnTwo()
    {
        Assert.Equal("RIGA, LV-1050", Parse("Kr. Barona street 7\nRIGA, LV-1050", null).ToString(AddressStyle.Postal).Split('\n')[1]);
        Assert.Equal("RIGA, LV-1050", Parse("Kr. Barona street 7\nRIGA\nLV-1050", null).ToString(AddressStyle.Postal).Split('\n')[1]);
        Assert.Equal("Riga, LV-1050", Parse("Kr. Barona street 7, Riga, LV-1050", "LV").ToString(AddressStyle.Display).Split('\n')[1]);
    }

    [Fact]
    public void ACountryNameBeatsAHintAndAPostcodeThatDoesNotFitItIsAConflict()
    {
        // The name on the address wins over a hint its postcode cannot belong to, and the disagreement is a warning; a postcode of another form than the named country's is a conflict.
        Assert.Equal(OperationStatus.Done, RuleAddressParser.TryParse("Kattstigen 5\n123 45 Täby\nSWEDEN", Region.Parse("DK"), out ParsedAddress? named, out ParseDetail warned));
        Assert.Equal(Region.Parse("SE"), named!.Country);
        Assert.Contains(warned.Issues, i => i.Reason.Equals(ReasonCode.AddressCountryConflict) && i.Severity == IssueSeverity.Warning);
        Assert.Equal(OperationStatus.InvalidData, RuleAddressParser.TryParse("Kattstigen 5\n1234 Täby\nSWEDEN", Region.Unknown, out _, out ParseDetail d));
        Assert.True(d.PrimaryReason.Equals(ReasonCode.AddressCountryConflict), d.PrimaryReason.ToString());
    }

    [Fact]
    public void LinesTheRulesCannotPlaceGoToTheModel()
    {
        // No number after the name, and a number-less line is a place or a person: nothing is invented.
        Assert.Equal(OperationStatus.InvalidData, RuleAddressParser.TryParse("Storgata, Kjeller 15 Oslo avdeling\n0161 Oslo", Region.Parse("NO"), out _, out ParseDetail d));
        Assert.True(d.PrimaryReason.Equals(ReasonCode.AddressModelRequired) || d.PrimaryReason.Equals(ReasonCode.AddressNoDeliveryLine), d.PrimaryReason.ToString());
    }

    [Fact]
    public void PostalAddressRendersBothStylesAndKeepsTheCountry()
    {
        Assert.Equal(OperationStatus.Done, PostalAddress.TryParse("Kattstigen 5 lgh 1234, 182 46 Täby", Region.Parse("SE"), out PostalAddress a, out _));
        Assert.Equal("Kattstigen 5 lgh 1234, 182 46 TÄBY", a.ToString(AddressStyle.Postal, AddressLayout.SingleLine));
        Assert.Equal("Kattstigen 5 lgh 1234, 182 46 Täby", a.ToString(AddressStyle.Display, AddressLayout.SingleLine));
    }

    [Fact]
    public void ExpansionsKnowTheStreetTypesOfTheNorthernLanguages()
    {
        Assert.Contains("narva maantee 7", AddressExpander.Expand("Narva mnt 7", ExpansionLanguages.Estonian), StringComparer.Ordinal);
        Assert.Contains("gedimino prospektas 9", AddressExpander.Expand("Gedimino pr 9", ExpansionLanguages.Lithuanian), StringComparer.Ordinal);
        Assert.Contains("karl johans gate 5", AddressExpander.Expand("Karl Johans gt 5", ExpansionLanguages.Norwegian), StringComparer.Ordinal);
    }

    [Fact]
    public void KeysIgnoreWrittenAbbreviationsOfTheCountrysLanguage()
    {
        Assert.Equal(OperationStatus.Done, PostalAddress.TryParse("Narva mnt 7\n10117 Tallinn", Region.Parse("EE"), out PostalAddress a, out _));
        Assert.Equal(OperationStatus.Done, PostalAddress.TryParse("NARVA MAANTEE 7\n10117 TALLINN", Region.Parse("EE"), out PostalAddress b, out _));
        Assert.Equal(RecursiveAI.OneContact.Keys.AddressKey.Compute(a, RecursiveAI.OneContact.KeyVersion.V1), RecursiveAI.OneContact.Keys.AddressKey.Compute(b, RecursiveAI.OneContact.KeyVersion.V1));
    }

    private static HashSet<string> Labelled(string text, string hint) => [.. Parse(text, hint).Fields.Select(f => f.Name + "=" + f.Text)];

    [Theory]
    [InlineData("Mads Hansen\nÆblehaven, 1\nDK-1352 København K", "DK", "country=DK", "postal-code=1352")]
    [InlineData("Fjärdgränden 21 lgh 1103, 575 33 EKSJÖ", "SE", "street.unit-designator=lgh", "street.unit-number=1103")]
    [InlineData("Nielsen Byg ApS Amagerbrogade 21 2640 Hedehusene", "DK", "leading-line=Nielsen Byg ApS", "street.name=Amagerbrogade")]
    [InlineData("Upes iela 1\nMazsalaca, Valmieras nov., LV-4215", "LV", "locality=Mazsalaca", "admin-area=Valmieras nov.")]
    [InlineData("Tehnika tn 24\nPõltsamaa vald\n48103 Põltsamaa Jõgeva maakond", "EE", "dependent-locality=Põltsamaa vald", "admin-area=Jõgeva maakond")]
    [InlineData("Vītola Līga\nRīvēni\nMatīšu pag.\nMatīši, Valmieras nov., LV-4210", "LV", "street.name=Rīvēni", "dependent-locality=Matīšu pag.")]
    [InlineData("VEI 238 39B\n9170 LONGYEARBYEN", "NO", "street.name=VEI 238", "street.house-number=39B")]
    [InlineData("Alasi tn 32, korter 304, 50109 Tartu", "EE", "street.unit-designator=korter", "street.unit-number=304")]
    [InlineData("Nomāļu iela 2 - 22\nTalsi, Talsu nov., LV-3201", "LV", "street.house-number=2", "street.unit-number=22")]
    public void FieldsAreLabelledAsTheRegistersDo(string text, string hint, string first, string second)
    {
        // A country code before the postcode is the country; a dash, lgh, dz. or korter splits the apartment from the house number; a parish, municipality or county is its own field.
        HashSet<string> fields = Labelled(text, hint);
        Assert.Contains(first, fields);
        Assert.Contains(second, fields);
    }

    [Fact]
    public void ASplitLatvianLocalityLineIsWrittenOnOneLine()
    {
        Assert.Equal("MAZSALACA, VALMIERAS NOV., LV-4215", Parse("Upes iela 1\nMazsalaca, Valmieras nov., LV-4215", "LV").ToString(AddressStyle.Postal).Split('\n')[1]);
        Assert.Equal("48103 PÕLTSAMAA JÕGEVA MAAKOND", Parse("Tehnika tn 24\n48103 Põltsamaa Jõgeva maakond", "EE").ToString(AddressStyle.Postal).Split('\n')[1]);
    }
}
