using System.Buffers;
using RecursiveAI.OneContact.Addresses;
using RecursiveAI.OneContact.Addresses.Expansion;
using RecursiveAI.OneContact.Addresses.Parsing;
using RecursiveAI.OneContact.Addresses.Standardization;
using RecursiveAI.OneContact.Keys;

namespace RecursiveAI.OneContact.Tests.Addresses;

/// <summary>
/// Every example address of the free guides of the first Tier 2 group (La Poste, bpost, the UPU country sheets for the Netherlands, Luxembourg, Germany, Austria, Switzerland and Monaco),
/// as transcribed in testdata/addresses/authority-examples-tier2a.tsv with its citation, and the rules those guides state.
/// </summary>
public class Tier2AuthorityTests
{
    public static TheoryData<string, string, string, string, string, string, string> Cases()
    {
        TheoryData<string, string, string, string, string, string, string> data = [];
        foreach (string[] c in AddressTestData.Rows("testdata/addresses/authority-examples-tier2a.tsv", 7))
        {
            data.Add(c[0], c[1], c[2], c[3], c[4].Replace("\\n", "\n", StringComparison.Ordinal), c[5].Replace("\\n", "\n", StringComparison.Ordinal), c[6].Replace("\\n", "\n", StringComparison.Ordinal));
        }

        return data;
    }

    // The guides print one or two spaces between a Dutch postcode and its locality; the Dutch rule is checked on its own.
    private static string Squash(string s) => string.Join('\n', s.Split('\n').Select(l => string.Join(' ', l.Split(' ', StringSplitOptions.RemoveEmptyEntries))));

    [Theory]
    [MemberData(nameof(Cases))]
    public void ExampleIsStandardized(string id, string citation, string hint, string withCountry, string input, string postal, string display)
    {
        Region region = hint.Length == 0 ? Region.Unknown : Region.Parse(hint);
        OperationStatus status = RuleAddressParser.TryParse(input, region, out ParsedAddress? address, out ParseDetail detail);
        Assert.True(status == OperationStatus.Done, $"{id} ({citation}) was not read: {detail.PrimaryReason}");
        bool country = withCountry == "1";
        Assert.Equal(Squash(postal), Squash(address!.ToString(AddressStyle.Postal, AddressLayout.Label, country)));
        if (display != "=")
        {
            Assert.Equal(Squash(display), Squash(address.ToString(AddressStyle.Display, AddressLayout.Label, country)));
        }
    }

    [Fact]
    public void DutchPostcodeIsFollowedByTwoSpaces()
    {
        // UPU Netherlands sheet: two spaces between the postcode and the locality.
        Assert.True(RuleAddressParser.TryParse("Mr. J. de Vries\nAdrianastraat 34\n6832 AM Arnhem", Region.Unknown, out ParsedAddress? a, out _) == OperationStatus.Done);
        Assert.Equal("6832 AM  ARNHEM", a!.ToString(AddressStyle.Postal).Split('\n')[^1]);
        Assert.Equal("6832 AM Arnhem", a.ToString(AddressStyle.Display).Split('\n')[^1]);
        Assert.Equal(Region.Parse("NL"), a.Country);
    }

    [Fact]
    public void FrenchTypeIsAbbreviatedOnlyWhenTheLineIsLongerThan38Characters()
    {
        // SP 8855 volume 2, 3.1: 38 characters at most per line; 3.3: the authorized abbreviations. UPU France: the types are written in full when the line fits.
        Parse("12 AV DU MARECHAL DE LATTRE DE TASSIGNY\n75016 PARIS", "FR", out ParsedAddress? shortLine);
        Assert.Equal("12 AV DU MARECHAL DE LATTRE DE TASSIGNY", shortLine!.ToString(AddressStyle.Postal).Split('\n')[0]);
        Parse("12 avenue du Roi\n75016 PARIS", "FR", out ParsedAddress? fits);
        Assert.Equal("12 AVENUE DU ROI", fits!.ToString(AddressStyle.Postal).Split('\n')[0]);
        Parse("12 av du roi\n75016 PARIS", "FR", out ParsedAddress? expanded);
        Assert.Equal("12 AVENUE DU ROI", expanded!.ToString(AddressStyle.Postal).Split('\n')[0]);
    }

    [Fact]
    public void FrenchSaintInTheLocalityLineIsAbbreviated()
    {
        // UPU France sheet: SAINT and SAINTE of the locality line are written ST and STE.
        Parse("3 RUE DU PORT\n33500 SAINT-ANDRE-DE-CUBZAC", "FR", out ParsedAddress? a);
        Assert.Equal("33500 ST ANDRE DE CUBZAC", a!.ToString(AddressStyle.Postal).Split('\n')[^1]);
        Parse("3 RUE DU PORT\n33500 SAINTE FOY", "FR", out ParsedAddress? b);
        Assert.Equal("33500 STE FOY", b!.ToString(AddressStyle.Postal).Split('\n')[^1]);
    }

    [Fact]
    public void BelgianBoxLiteralIsRewrittenToBusBteOrBox()
    {
        // bpost template 3: the box number is preceded by bus, bte or box; b, Bt, #, - and / are not allowed.
        Parse("Rue du Vivier 7 B 5\n1000 Bruxelles", "BE", out ParsedAddress? fr);
        Assert.Equal("Rue du Vivier 7 bte 5", fr!.ToString(AddressStyle.Postal).Split('\n')[0]);
        Parse("Kortijkstraat 37 Bt 1\n9800 Deinze", "BE", out ParsedAddress? nl);
        Assert.Equal("Kortijkstraat 37 bus 1", nl!.ToString(AddressStyle.Postal).Split('\n')[0]);
        Parse("Kortijkstraat 37 bus 1\n9800 Deinze", "BE", out ParsedAddress? kept);
        Assert.Equal("Kortijkstraat 37 bus 1", kept!.ToString(AddressStyle.Postal).Split('\n')[0]);
    }

    [Fact]
    public void BelgianBoxNumberHasNoSpaces()
    {
        // bpost template 3: Postbus 12000 and not Postbus 12 000.
        Parse("Postbus 12 000\n9000 Gent", "BE", out ParsedAddress? a);
        Assert.Equal("Postbus 12000", a!.ToString(AddressStyle.Postal).Split('\n')[0]);
    }

    [Fact]
    public void BelgianStreetTypeIsAbbreviatedOnlyBeyond50Characters()
    {
        // bpost MassPost 2025, 4: abbreviations only when the number of characters per line exceeds 50.
        Parse("Burgemeester Doctor Alexander Vandenbosschestraat 123 bus 12\n9000 Gent", "BE", out ParsedAddress? a);
        string line = a!.ToString(AddressStyle.Postal).Split('\n')[0];
        Assert.Equal("Burgemeester Doctor Alexander Vandenbosschestr. 123 bus 12", line);
        Parse("Burgemeestersstraat 1\n9000 Gent", "BE", out ParsedAddress? b);
        Assert.Equal("Burgemeestersstraat 1", b!.ToString(AddressStyle.Postal).Split('\n')[0]);
    }

    [Fact]
    public void CountryComesFromNameOrPrefixOrPostcodeForm()
    {
        Assert.Equal(Region.Parse("FR"), Parse("92 avenue des Champs Elysees\n75008 Paris\nFrance", null, out ParsedAddress? fr).Country);
        Assert.False(fr!.CountryInferred);
        Assert.Equal(Region.Parse("LU"), Parse("4 rue des Prés\nL-0111 Remich", null, out _).Country);
        Assert.Equal(Region.Parse("NL"), Parse("Adrianastraat 34\n6832 AM Arnhem", null, out _).Country);
        Assert.Equal(Region.Parse("MC"), Parse("1 avenue de l Hermitage\n98000 Monaco", null, out _).Country);
        Assert.Equal(Region.Parse("DE"), Parse("Wacholderweg 52a\n26133 Oldenburg", null, out _).Country);
        Assert.Equal(Region.Parse("FR"), Parse("25 rue des Fleurs\n33500 Libourne", null, out _).Country);
        Assert.Equal(Region.Parse("CH"), Parse("Rue du Marché 8\n1556 Cerniaz VD", null, out _).Country);
    }

    [Fact]
    public void AmbiguousPostcodeWithoutHintDoesNotGuessACountry()
    {
        // Four digits are a postcode in Belgium, Austria, Switzerland, Liechtenstein and Luxembourg; the rules do not guess: the fields come back with the country left unknown
        // when every country that can read the text finds the same ones, and the text is refused when they disagree.
        Assert.True(AddressTestData.NamesNoCountry("Hauptplatz 1\n1010 Wien"));
    }

    [Fact]
    public void UnitedStatesAndCanadaAreReadFirstWhenNothingNamesAnotherCountry()
    {
        Assert.Equal(Region.US, Parse("123 Main St\nSpringfield VA 22162", null, out _).Country);
        Assert.Equal(Region.CA, Parse("123 Main St\nMontreal QC H3Z 2Y7", null, out _).Country);
    }

    [Fact]
    public void OverseasDepartmentHintIsKept()
    {
        Parse("12 RUE DU PORT\n97100 BASSE TERRE", "GP", out ParsedAddress? a);
        Assert.Equal(Region.Parse("GP"), a!.Country);
        Assert.Equal("GUADELOUPE", a.ToString(AddressStyle.Postal, AddressLayout.Label, includeCountry: true).Split('\n')[^1]);
    }

    [Fact]
    public void HintThatThePostcodeCannotBelongToIsRefused()
    {
        // Five digits cannot be a Belgian postcode.
        OperationStatus s = RuleAddressParser.TryParse("Rue du Vivier 7\n75008 Paris", Region.Parse("BE"), out _, out ParseDetail d);
        Assert.Equal(OperationStatus.InvalidData, s);
        Assert.Equal(ReasonCode.AddressCountryConflict, d.PrimaryReason);
    }

    [Fact]
    public void SingleLineIsReadAndOffsetsPointIntoTheInput()
    {
        const string text = "Jean Durand, 25 rue des Fleurs, 33500 Libourne";
        Parse(text, "FR", out ParsedAddress? a);
        Assert.Equal("JEAN DURAND, 25 RUE DES FLEURS, 33500 LIBOURNE", a!.ToString());
        foreach (ParsedAddressField f in a.Fields)
        {
            Assert.Equal(f.Text, text.Substring(f.Span.Start, f.Span.Length));
        }

        Assert.Equal("33500", a.PostalCode!.Value.Text);
        Assert.Equal("LIBOURNE", a.Locality!.Value.Standard);
    }

    [Fact]
    public void NothingIsInvented()
    {
        // A postcode never supplies a locality, and a line the rules cannot place goes to the model (refused here).
        OperationStatus s = RuleAddressParser.TryParse("33500", Region.Parse("FR"), out _, out _);
        Assert.Equal(OperationStatus.InvalidData, s);
        s = RuleAddressParser.TryParse("Villa Rose, Les Pins, 3 bis, 06000 Nice", Region.Parse("FR"), out ParsedAddress? a, out _);
        Assert.Equal(OperationStatus.Done, s);
        Assert.DoesNotContain("PARIS", a!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void PostalAddressUsesTheRules()
    {
        Assert.True(PostalAddress.TryParse("25 rue des Fleurs\n33500 Libourne", Region.Parse("FR"), out PostalAddress p, out _) == OperationStatus.Done);
        Assert.Equal(Region.Parse("FR"), p.Country);
        Assert.Equal("25 RUE DES FLEURS, 33500 LIBOURNE", p.Canonical);
        Assert.Equal("25 Rue des Fleurs\n33500 Libourne", p.ToString(AddressStyle.Display));
        Assert.Equal("33500", p.PostalCode);
        Assert.Equal("Libourne", p.Locality);
    }

    [Fact]
    public void KeysAgreeAcrossAbbreviationsAndLanguages()
    {
        Assert.Equal(Key("12 av du roi\n75016 Paris", "FR"), Key("12 AVENUE DU ROI\n75016 PARIS", "FR"));
        Assert.Equal(Key("Hauptstr. 5\n26133 Oldenburg", "DE"), Key("Hauptstrasse 5\n26133 OLDENBURG", "DE"));
        Assert.Equal(Key("Kortijkstraat 37 bus 1\n9800 Deinze", "BE"), Key("Kortijkstr. 37 box 1\n9800 DEINZE", "BE"));
        Assert.NotEqual(Key("Hauptstr. 5\n26133 Oldenburg", "DE"), Key("Hauptstr. 6\n26133 Oldenburg", "DE"));
        Assert.StartsWith("fr|", Key("12 av du roi\n75016 Paris", "FR"), StringComparison.Ordinal);
    }

    [Fact]
    public void ExpansionKnowsGermanDutchItalianAndLuxembourgish()
    {
        IReadOnlyList<string> de = AddressExpander.Expand("Hauptstr", ExpansionLanguages.German);
        Assert.True(de.Contains("hauptstrasse"), string.Join(" | ", de));
        IReadOnlyList<string> nl = AddressExpander.Expand("Kerkstr", ExpansionLanguages.Dutch);
        Assert.True(nl.Contains("kerkstraat"), string.Join(" | ", nl));
        Assert.NotEmpty(AddressExpander.Expand("via Roma 5", ExpansionLanguages.Italian | ExpansionLanguages.Luxembourgish));
        Assert.Contains("12 avenue", AddressExpander.Expand("12 av", ExpansionLanguages.French));
        Assert.Contains("hauptstr", AddressExpander.Expand("Hauptstr", ExpansionLanguages.German | ExpansionLanguages.Dutch));
    }

    private static ParsedAddress Parse(string text, string? hint, out ParsedAddress? address)
    {
        OperationStatus status = RuleAddressParser.TryParse(text, hint is null ? Region.Unknown : Region.Parse(hint), out address, out ParseDetail detail);
        Assert.True(status == OperationStatus.Done, $"not read: {detail.PrimaryReason} for {text}");
        return address!;
    }

    private static string Key(string text, string hint)
    {
        Assert.True(PostalAddress.TryParse(text, Region.Parse(hint), out PostalAddress p, out ParseDetail d) == OperationStatus.Done, $"{text}: {d.PrimaryReason}");
        return AddressKey.Compute(p, KeyVersion.V1);
    }
}
