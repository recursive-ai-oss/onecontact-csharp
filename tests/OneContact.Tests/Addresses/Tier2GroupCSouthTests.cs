using System.Buffers;
using RecursiveAI.OneContact.Addresses;
using RecursiveAI.OneContact.Addresses.Expansion;
using RecursiveAI.OneContact.Addresses.Parsing;
using RecursiveAI.OneContact.Addresses.Standardization;
using RecursiveAI.OneContact.Addresses.Streets;
using RecursiveAI.OneContact.Keys;

namespace RecursiveAI.OneContact.Tests.Addresses;

/// <summary>
/// Every example address of the free guides of the last group of Tier 2 countries (Poland, the Czech Republic, Slovakia, Slovenia, Croatia, Hungary, Romania, Mexico and Australia:
/// the UPU country sheets, Czech Post's address formats and Australia Post's two guides), as transcribed in testdata/addresses/authority-examples-tier2c-south.tsv with its
/// citation, and the rules those guides state.
/// </summary>
public class Tier2GroupCSouthTests
{
    public static TheoryData<string, string, string, string, string, string, string> Cases()
    {
        TheoryData<string, string, string, string, string, string, string> data = [];
        foreach (string[] c in AddressTestData.Rows("testdata/addresses/authority-examples-tier2c-south.tsv", 7))
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
        foreach (ParsedAddressField f in address.Fields)
        {
            Assert.Equal(f.Text, input.Substring(f.Span.Start, f.Span.Length));
        }

        // The same text is also read without a hint when it carries the evidence itself: a printed country line, or Australia's state before the postcode.
        if (country || hint == "AU")
        {
            Assert.True(RuleAddressParser.TryParse(input, Region.Unknown, out ParsedAddress? unhinted, out ParseDetail d2) == OperationStatus.Done, $"{id} without a hint: {d2.PrimaryReason}");
            Assert.Equal(region, unhinted!.Country);
            Assert.Equal(postal, unhinted.ToString(AddressStyle.Postal, AddressLayout.Label, country));
        }
    }

    private static ParsedAddress Parse(string text, string? hint)
    {
        OperationStatus status = RuleAddressParser.TryParse(text, hint is null ? Region.Unknown : Region.Parse(hint), out ParsedAddress? a, out ParseDetail d);
        Assert.True(status == OperationStatus.Done, $"not read: {d.PrimaryReason}");
        return a!;
    }

    private static PostalAddress PostalOf(string text, string hint)
    {
        Assert.True(PostalAddress.TryParse(text, Region.Parse(hint), out PostalAddress p, out ParseDetail d) == OperationStatus.Done, $"{text}: {d.PrimaryReason}");
        return p;
    }

    private static string Key(string text, string hint)
    {
        Assert.True(PostalAddress.TryParse(text, Region.Parse(hint), out PostalAddress p, out ParseDetail d) == OperationStatus.Done, $"{text}: {d.PrimaryReason}");
        return AddressKey.Compute(p, KeyVersion.V1);
    }

    [Theory]
    [InlineData("Jan Kowalski, ul. Długa 5/12, 31-147 Kraków", "Jan Kowalski\nul. Długa 5/12\n31-147 KRAKÓW")]
    [InlineData("Jan Kowalski\nul. 3 Maja 12\n31-147 Kraków", "Jan Kowalski\nul. 3 Maja 12\n31-147 KRAKÓW")]
    [InlineData("Jan Kowalski\nRynek 12\n31-042 Kraków", "Jan Kowalski\nRynek 12\n31-042 KRAKÓW")]
    [InlineData("Biuro\nskr. poczt. Nr 115\n48-300 Nysa", "Biuro\nskr. poczt. Nr 115\n48-300 NYSA")]
    public void PolishLinesAreReadAsTheGuidesWriteThem(string input, string postal)
    {
        // Poczta Polska: name, street type and name with the number, postcode and locality; "skr. poczt. Nr ..." names a box. The postcode is XX-XXX and the locality is capitals.
        ParsedAddress a = Parse(input, "PL");
        Assert.Equal(postal, a.ToString(AddressStyle.Postal));
    }

    [Fact]
    public void PolishPostcodeIsReadWithOrWithoutTheCountryLetters()
    {
        Assert.Equal("00-950", Parse("Ul. Kręta 15m 10\nPL-00-950 Warszawa", null).PostalCode!.Value.Standard);
        Assert.Equal(Region.Parse("PL"), Parse("Ul. Kręta 15m 10, 00-950 Warszawa", null).Country);
    }

    [Fact]
    public void CzechPostcodeAndLocalityAreSeparatedByTwoSpaces()
    {
        // UPU Czech Republic sheet: one space between the third and fourth digit and two spaces between the postcode and the locality name.
        ParsedAddress a = Parse("Pan Martin Parma\nLadova 3/64\n251 66 Hrusice", "CZ");
        Assert.Equal("251 66  HRUSICE", a.ToString(AddressStyle.Postal).Split('\n')[^1]);
        Assert.Equal("251 66 Hrusice", a.ToString(AddressStyle.Display).Split('\n')[^1]);
        Assert.Equal("251 66", a.PostalCode!.Value.Standard);
    }

    [Fact]
    public void CzechAndSlovakPostcodesNeedACountryToTellThemApart()
    {
        // The form is the same: without a country line, prefix or hint the text is not guessed.
        Assert.Equal(OperationStatus.InvalidData, RuleAddressParser.TryParse("Ladova 3/64\n251 66 Hrusice", Region.Unknown, out _, out ParseDetail d));
        Assert.Equal(ReasonCode.AddressCountryUndetermined, d.PrimaryReason);
        Assert.Equal(Region.Parse("SK"), Parse("Ladova 3/64\nSK-251 66 Hrusice", null).Country);
    }

    [Fact]
    public void CzechDateOfBirthLineIsNeverTheStreetLine()
    {
        // Czech Post's format for a person with a date of birth: "nar. 1. 4. 1946" stands above the street line.
        ParsedAddress a = Parse("Paní\nBožena Novotná\nnar. 1. 4. 1946\nStavbařů 4211\n190 16 PRAHA 916", "CZ");
        Assert.Equal("Stavbařů", a.DeliveryLines[0].Parts.First(p => p.Role == StreetPartRole.StreetName).Standard);
        Assert.Equal(3, a.LeadingLines.Count);
    }

    [Fact]
    public void CzechSquareNameKeepsItsTypeAsAPart()
    {
        ParsedAddress a = Parse("Valdštejnské náměstí 17/4\n118 00 Praha 1", "CZ");
        Assert.Contains(a.DeliveryLines[0].Parts, p => p.Role == StreetPartRole.StreetType && p.Standard == "náměstí");
        Assert.Contains(a.DeliveryLines[0].Parts, p => p.Role == StreetPartRole.HouseNumber && p.Standard == "17/4");
    }

    [Fact]
    public void CroatianPostcodeNeedsItsPrefixOrAWordOfTheLanguage()
    {
        Assert.Equal(Region.Parse("HR"), Parse("Ulica kneza Branimira 4\n10000 Zagreb", null).Country);
        Assert.Equal(Region.Parse("HR"), Parse("Krapinska 17\nHR-10000 Zagreb", null).Country);
        Assert.Equal(OperationStatus.InvalidData, RuleAddressParser.TryParse("Krapinska 17\n10000 Zagreb", Region.Unknown, out _, out ParseDetail d));
        Assert.Equal(ReasonCode.AddressCountryUndetermined, d.PrimaryReason);
    }

    [Fact]
    public void HungarianInformalOrderIsPrintedInTheAuthoritysOrder()
    {
        // Magyar Posta and the UPU Hungary sheet: recipient, locality, street line, postcode on a line of its own.
        const string text = "Kovács Anna, 1037 Budapest, Virág tér 3. IV. 61";
        ParsedAddress a = Parse(text, null);
        Assert.Equal(Region.Parse("HU"), a.Country);
        Assert.Equal("Kovács Anna\nBUDAPEST\nVirág tér 3. IV. 61\n1037", a.ToString(AddressStyle.Postal));
        Assert.Equal("Budapest", a.Locality!.Value.Text);
        foreach (ParsedAddressField f in a.Fields)
        {
            Assert.Equal(f.Text, text.Substring(f.Span.Start, f.Span.Length));
        }

        Assert.Equal("Kovács Anna, Budapest, Virág tér 3. IV. 61, 1037", a.ToString(AddressStyle.Display, AddressLayout.SingleLine));
    }

    [Fact]
    public void HungarianAuthorityOrderOnOneLine()
    {
        ParsedAddress a = Parse("Máté József, Budapest, Virág tér 3. IV. 61, 1037", null);
        Assert.Equal(Region.Parse("HU"), a.Country);
        Assert.Equal("Máté József\nBUDAPEST\nVirág tér 3. IV. 61\n1037", a.ToString(AddressStyle.Postal));
    }

    [Fact]
    public void FourDigitsAloneAreNotHungarianWithoutAHungarianStreetWord()
    {
        Assert.Equal(OperationStatus.InvalidData, RuleAddressParser.TryParse("Jan Janssens\nBrussel\nKerkstraat 3\n1000", Region.Unknown, out _, out _));
    }

    [Fact]
    public void RomanianNumberMarkerAndSubBuildingInformationAreParts()
    {
        ParsedAddress a = Parse("Str. Bariera Valcii, nr. 215, 200716 Craiova", "RO");
        Assert.Equal("Str. Bariera Valcii, nr. 215\n200716 CRAIOVA", a.ToString(AddressStyle.Postal));
        Assert.Contains(a.DeliveryLines[0].Parts, p => p.Role == StreetPartRole.HouseNumber && p.Standard == "215");
        ParsedAddress b = Parse("ASTRA, Drumul Taberei nr. 35, bl. F5, sc. 2, parter, ap. 23, Sector 6, 061357 Bucuresti", "RO");
        Assert.Equal("ASTRA\nDrumul Taberei nr. 35, bl. F5, sc. 2, parter, ap. 23\nSector 6\n061357 BUCURESTI", b.ToString(AddressStyle.Postal));
    }

    [Fact]
    public void RomanianKeyIgnoresTheNumberMarker()
    {
        Assert.Equal(Key("Str. Bariera Valcii, nr. 215, 200716 Craiova", "RO"), Key("Strada Bariera Valcii 215\n200716 Craiova", "RO"));
    }

    [Fact]
    public void MexicanStateOnItsOwnUnitOfSingleLineText()
    {
        const string text = "Alonso Reyes Díaz, Av. Reforma #456, Col. Juárez, 06600 Ciudad de México, CDMX";
        ParsedAddress a = Parse(text, null);
        Assert.Equal(Region.Parse("MX"), a.Country);
        Assert.Equal("CDMX", a.AdminArea!.Value.Standard);
        Assert.Equal("Alonso Reyes Díaz\nAv. Reforma #456\nCol. Juárez\n06600 CIUDAD DE MÉXICO, CDMX", a.ToString(AddressStyle.Postal));
        foreach (ParsedAddressField f in a.Fields)
        {
            Assert.Equal(f.Text, text.Substring(f.Span.Start, f.Span.Length));
        }
    }

    [Fact]
    public void MexicanStateNameIsPrintedAsTheSheetsAbbreviation()
    {
        ParsedAddress a = Parse("Calle 20 de Noviembre 123\nCol. Centro\n44100 Guadalajara, Jalisco\nMéxico", null);
        Assert.Equal("JAL", a.AdminArea!.Value.Standard);
        Assert.Equal("44100 GUADALAJARA, JAL", a.ToString(AddressStyle.Postal).Split('\n')[^1]);
    }

    [Fact]
    public void MexicoNeedsItsOwnEvidenceFromFiveDigits()
    {
        Assert.Equal(OperationStatus.InvalidData, RuleAddressParser.TryParse("Calle 20 de Noviembre 123\n44100 Guadalajara", Region.Unknown, out _, out _));
        Assert.Equal(Region.Parse("MX"), Parse("Calle 20 de Noviembre 123\n44100 Guadalajara", "MX").Country);
    }

    [Theory]
    [InlineData("Unit 4, 12 Smith St, Sydney NSW 2000", "UNIT 4 12 SMITH ST\nSYDNEY NSW 2000")]
    [InlineData("4/12 Smith St, Sydney NSW 2000", "4/12 SMITH ST\nSYDNEY NSW 2000")]
    [InlineData("Level 3, 100 George Street, Sydney NSW 2000", "LEVEL 3 100 GEORGE STREET\nSYDNEY NSW 2000")]
    [InlineData("Mr A Jones, Shop 2, Level 1, 17-19 Collins St., Melbourne VIC 3000", "Mr A Jones\nSHOP 2 LEVEL 1 17-19 COLLINS ST\nMELBOURNE VIC 3000")]
    public void AustralianUnitAndLevelFormsAreOneStreetLine(string input, string postal)
    {
        // Australia Post, Address Presentation Standards p. 14-17: sub-unit and level come before the street number on the street line.
        Assert.Equal(postal, Parse(input, null).ToString(AddressStyle.Postal));
    }

    [Fact]
    public void AustralianPartsAreSeparated()
    {
        ParsedAddress a = Parse("Unit 4\n12 Smith St W\nSYDNEY NSW 2000", "AU");
        // "Unit 4" alone above the street number is the guide's alternative (a separate line above the street line): it is kept as written.
        Assert.Equal("Unit 4\n12 SMITH ST W\nSYDNEY NSW 2000", a.ToString(AddressStyle.Postal));
        ParsedAddress b = Parse("4/12 Smith St W, SYDNEY NSW 2000", "AU");
        // The slash form is a unit number and a street number that print as written ("4/12").
        StreetRoles(b, StreetPartRole.UnitNumber, StreetPartRole.HouseNumber, StreetPartRole.StreetName, StreetPartRole.StreetType, StreetPartRole.PostDirectional);
        Assert.Equal("NSW", b.AdminArea!.Value.Standard);
        Assert.Equal("4/12 SMITH ST W\nSYDNEY NSW 2000", b.ToString(AddressStyle.Postal));
    }

    [Theory]
    [InlineData("97 Roberts Circuit, Camira Queensland 4300", "97 ROBERTS CIRCUIT\nCAMIRA QLD 4300")]
    [InlineData("187 Wilson Esplanade Downer, ACT 2602", "187 WILSON ESPLANADE\nDOWNER ACT 2602")]
    [InlineData("12 Smith St\nSydney nsw 2000", "12 SMITH ST\nSYDNEY NSW 2000")]
    public void AustralianStateInFullOrOnOneLine(string input, string postal) =>
        Assert.Equal(postal, Parse(input, "AU").ToString(AddressStyle.Postal));

    [Fact]
    public void RomanianCountyIsTheAdminAreaAndCommaBelowLettersFold()
    {
        ParsedAddress a = Parse("Strada Sion Gheorghe 67, 400241 Cluj-Napoca, județul Cluj", "RO");
        Assert.Equal("județul Cluj", a.AdminArea!.Value.Standard);
        Assert.Equal("Cluj-Napoca", a.Locality!.Value.Text);
        Assert.Equal("400241 CLUJ-NAPOCA, JUDEȚUL CLUJ", a.ToString(AddressStyle.Postal).Split('\n')[^1]);
        Assert.Equal(AddressExpander.Fold("Județ Ștefan Țepeș"), AddressExpander.Fold("Judet Stefan Tepes"));
    }

    [Theory]
    [InlineData("Strada Televiziunii, nr. 41, 062362 București, Sector 6", "RO")]
    [InlineData("Strada Rosetti C. A. 26\nPitești, 110040", "RO")]
    [InlineData("Intrarea Versului, 18, Râmnicu Vâlcea, 240192", "RO")]
    public void RomanianPostcodeLastAndSectorTail(string input, string hint) =>
        Assert.Equal(Region.Parse("RO"), Parse(input, hint).Country);

    private static void StreetRoles(ParsedAddress a, params StreetPartRole[] roles) =>
        Assert.Equal(roles, a.DeliveryLines[0].Parts.Select(p => p.Role).ToArray());

    [Fact]
    public void AustralianPostcodeAndStateAreValidatedStructurally()
    {
        PostalAddress ok = PostalOf("12 Smith St\nSYDNEY NSW 2000", "AU");
        Assert.Empty(ok.Validate());
        PostalAddress wrong = PostalOf("12 Smith St\nSYDNEY NSW 3000", "AU");
        Assert.NotEmpty(wrong.Validate());
        PostalAddress mx = PostalOf("Calle 20 de Noviembre 123\n44100 GUADALAJARA, JAL", "MX");
        Assert.Empty(mx.Validate());
        PostalAddress mxWrong = PostalOf("Calle 20 de Noviembre 123\n06600 GUADALAJARA, JAL", "MX");
        Assert.NotEmpty(mxWrong.Validate());
    }

    [Fact]
    public void AustralianStateMustBeCapitals()
    {
        Assert.Equal(OperationStatus.InvalidData, RuleAddressParser.TryParse("12 Smith St\nSydney nsw 2000", Region.Unknown, out _, out _));
    }

    [Theory]
    [InlineData("12 Smith St\nSYDNEY NSW 2000\nAUSTRALIA", "AU")]
    [InlineData("12 Smith St\nSYDNEY NSW 2000", "")]
    public void AustraliaIsReadFromTheStateAndThePostcode(string text, string hint) =>
        Assert.Equal(Region.Parse("AU"), Parse(text, hint.Length == 0 ? null : hint).Country);

    [Theory]
    [InlineData("Hrušovská 320/62\n102 00 Praha", "CZ", "Hrušovská 320/62\n102 00 Praha", "Hrusovska 320/62\n10200 praha")]
    public void CzechKeyFoldsDiacriticsAndPostcodeSpacing(string text, string hint, string same, string alsoSame)
    {
        string key = Key(text, hint);
        Assert.Equal(key, Key(same, hint));
        Assert.Equal(key, Key(alsoSame, hint));
    }

    [Fact]
    public void KeysOfDifferentAddressesDiffer()
    {
        Assert.NotEqual(Key("Hrušovská 320/62\n102 00 Praha", "CZ"), Key("Hrušovská 320/63\n102 00 Praha", "CZ"));
        Assert.Equal(Key("12 Smith Street\nSYDNEY NSW 2000", "AU"), Key("12 SMITH ST\nSYDNEY NSW 2000", "AU"));
    }

    [Theory]
    [InlineData(ExpansionLanguages.Polish, "ul. Kręta 5", "ulica")]
    [InlineData(ExpansionLanguages.Czech, "nám. Svobody 45", "náměstí")]
    [InlineData(ExpansionLanguages.Slovak, "Hlavná ul. 5", "ulica")]
    [InlineData(ExpansionLanguages.Slovenian, "Prešernova ul. 16", "ulica")]
    [InlineData(ExpansionLanguages.Croatian, "ul. Kneza Branimira 4", "ulica")]
    [InlineData(ExpansionLanguages.Romanian, "Str. Bariera Valcii 215", "strada")]
    [InlineData(ExpansionLanguages.Spanish, "Av. Insurgentes Sur 2455", "avenida")]
    [InlineData(ExpansionLanguages.English, "17 Jones St", "street")]
    public void ExpansionKnowsTheLanguagesStreetTypes(ExpansionLanguages language, string address, string expected) =>
        Assert.Contains(expected, AddressExpander.Expand(address, language)[0]);

    [Fact]
    public void HungarianTypeEndingAName()
    {
        // The libpostal Hungarian dictionary lists "u" and "utca" for the street type that ends a name.
        Assert.Contains("utca", AddressExpander.Expand("Fő u. 5", ExpansionLanguages.Hungarian)[0]);
    }

    [Theory]
    [InlineData("Poland")]
    [InlineData("zzz")]
    public void NonsenseIsRefusedNotInvented(string text) =>
        Assert.Equal(OperationStatus.InvalidData, RuleAddressParser.TryParse(text, Region.Parse("PL"), out _, out _));
}
