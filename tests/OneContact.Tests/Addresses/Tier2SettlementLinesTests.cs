using System.Buffers;
using RecursiveAI.OneContact.Addresses;
using RecursiveAI.OneContact.Addresses.Parsing;
using RecursiveAI.OneContact.Addresses.Standardization;
using RecursiveAI.OneContact.Keys;

namespace RecursiveAI.OneContact.Tests.Addresses;

/// <summary>
/// Settlement lines between the street and the postcode (Czech, Slovak and Croatian registers keep them as a dependent locality), the Bucharest sector, and the lines the rules
/// leave to the model because an addressee was written in front of the street name. All inputs are written for this repository.
/// </summary>
public class Tier2SettlementLinesTests
{
    private static ParsedAddress Read(string text, string hint)
    {
        OperationStatus status = RuleAddressParser.TryParse(text, Region.Parse(hint), out ParsedAddress? a, out ParseDetail d);
        Assert.True(status == OperationStatus.Done, $"{text}: {d.PrimaryReason}");
        foreach (ParsedAddressField f in a!.Fields)
        {
            Assert.Equal(f.Text, text.Substring(f.Span.Start, f.Span.Length));
        }

        return a;
    }

    private static string Field(ParsedAddress a, string name) => a.Fields.FirstOrDefault(f => f.Name == name).Text ?? string.Empty;

    [Theory]
    [InlineData("CZ", "Máchalova 116/15\nHodolany\n779 00 Olomouc", "Hodolany", "Olomouc")]
    [InlineData("CZ", "Filípkova 1412/13, Kateřinky, 747 05 Opava", "Kateřinky", "Opava")]
    [InlineData("CZ", "Petr Kučera\nMáchalova 116/15\nHodolany\n779 00 Olomouc", "Hodolany", "Olomouc")]
    [InlineData("SK", "Hlavná 12\nKrasňany\n010 01 Žilina", "Krasňany", "Žilina")]
    [InlineData("HR", "Bijela Stijena 30\nBijela Stijena\n35430 Okučani", "Bijela Stijena", "Okučani")]
    [InlineData("HR", "Stjepana Radića 85, Bicko Selo, 35212 Garčin", "Bicko Selo", "Garčin")]
    public void SettlementLineAfterTheStreetIsTheDependentLocality(string hint, string input, string dependent, string locality)
    {
        ParsedAddress a = Read(input, hint);
        Assert.Equal(dependent, Field(a, "dependent-locality"));
        Assert.Equal(locality, a.Locality!.Value.Text);
    }

    [Theory]
    [InlineData("CZ", "Žižkova 217/55\nPO Box 12\n747 07 Opava")]
    [InlineData("SK", "Hlavná 12\nBudova A\n010 01 Žilina")]
    [InlineData("CZ", "Žižkova 217/55\nnáměstí Míru\n747 07 Opava")]
    public void OtherLinesAreNotSettlements(string hint, string input) => Assert.Equal(string.Empty, Field(Read(input, hint), "dependent-locality"));

    [Fact]
    public void TheSettlementLineKeepsItsPlaceInTheLayout() =>
        Assert.Equal("Máchalova 116/15\nHodolany\n779 00 Olomouc", Read("Máchalova 116/15\nHodolany\n779 00 Olomouc", "CZ").ToString(AddressStyle.Display));

    // UPU Romania sheet: "011371 Bucureşti, Sector 1"; the town is the locality, the sector the dependent locality, and the printed line is unchanged.
    [Theory]
    [InlineData("Str. Ady Endre nr. 6\n011374 București, Sector 1", "București", "Sector 1")]
    [InlineData("Strada Gandhi Mahatma, nr. 3, Bucureşti, Sector 1, RO-011407", "Bucureşti", "Sector 1")]
    public void BucharestSectorIsTheDependentLocality(string input, string locality, string sector)
    {
        ParsedAddress a = Read(input, "RO");
        Assert.Equal(locality, a.Locality!.Value.Text);
        Assert.Equal(sector, Field(a, "dependent-locality"));
    }

    [Fact]
    public void TheSectorStaysInThePrintedLocalityLineAndInTheKey()
    {
        ParsedAddress a = Read("Str. Ady Endre nr. 6\n011374 București, Sector 1", "RO");
        string last = a.ToString(AddressStyle.Postal).Split('\n')[^1];
        Assert.StartsWith("011374 BUCURE", last, StringComparison.Ordinal);
        Assert.EndsWith("SECTOR 1", last, StringComparison.Ordinal);
        Assert.EndsWith("|bucuresti sector 1||011374", Key("Str. Ady Endre nr. 6\n011374 București, Sector 1"), StringComparison.Ordinal);
        Assert.NotEqual(Key("Str. Ady Endre nr. 6\n011374 București, Sector 1"), Key("Str. Ady Endre nr. 6\n011374 București, Sector 2"));
    }

    private static string Key(string text)
    {
        Assert.True(PostalAddress.TryParse(text, Region.Parse("RO"), out PostalAddress p, out ParseDetail d) == OperationStatus.Done, $"{text}: {d.PrimaryReason}");
        return AddressKey.Compute(p, KeyVersion.V1);
    }

    // The addressee written in front of the street name with no comma cannot be told from the street: the rules refuse and the model decides.
    [Theory]
    [InlineData("HU", "Balogh Z. Vaskút Akácfa utca 24. 6521 Magyarország")]
    [InlineData("HU", "István Molnár Duna Kft. Zrínyi Miklós utca 79 2233 Ecser")]
    [InlineData("DK", "Hans Jensen Vestergade 12, 8000 Aarhus C")]
    [InlineData("SE", "Anna Svensson Storgatan 5, 111 29 Stockholm")]
    [InlineData("NO", "Nils Larsen Nestangen 58, 1234 Oslo")]
    public void AnAddresseeGluedToTheStreetIsLeftToTheModel(string hint, string input) =>
        Assert.NotEqual(OperationStatus.Done, RuleAddressParser.TryParse(input, Region.Parse(hint), out _, out _));

    [Theory]
    [InlineData("HU", "Kossuth Lajos utca 12., 1051 Budapest")]
    [InlineData("HU", "Bartók B. utca 3\n1051 Budapest")]
    [InlineData("NO", "Karl Johans gate 12, 0154 Oslo")]
    [InlineData("NO", "Ole Bulls plass 3, 0160 Oslo")]
    [InlineData("SE", "Anna Svensson, Storgatan 5, 111 29 Stockholm")]
    [InlineData("DK", "Jens Otto Krags Gade 3, 8000 Aarhus C")]
    [InlineData("DK", "Hans Tausens Vænge 2, 8000 Aarhus C")]
    [InlineData("DK", "Jens Baggesens Allé 2, 8000 Aarhus C")]
    [InlineData("SE", "Carl Gustavs gränd 3, 111 29 Stockholm")]
    [InlineData("FI", "Paavo Nurmen katu 2, 00100 Helsinki")]
    public void StreetNamesWithPeopleNamesInThemAreStillRead(string hint, string input) =>
        Assert.Equal(OperationStatus.Done, RuleAddressParser.TryParse(input, Region.Parse(hint), out _, out _));
}
