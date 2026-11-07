using System.Buffers;
using RecursiveAI.OneContact.Addresses;
using RecursiveAI.OneContact.Addresses.Parsing;
using RecursiveAI.OneContact.Addresses.Standardization;
using RecursiveAI.OneContact.Addresses.Streets;

namespace RecursiveAI.OneContact.Tests.Addresses;

/// <summary>
/// The forms people write Hungarian, Romanian and Australian addresses in on the web (all inputs written for this repository), checked against the authorities' own layouts:
/// Magyar Posta's addressing notice and the UPU Hungary sheet, the UPU Romania sheet, Australia Post's Correct Addressing brochure and Address Presentation Standards.
/// </summary>
public class Tier2HuRoAuFormsTests
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

    // Hungary: the postcode and the locality come first and the street follows, with or without commas.
    [Theory]
    [InlineData("1068 Budapest Király utca 80.", "HU", "BUDAPEST\nKirály utca 80.\n1068")]
    [InlineData("1068 Budapest Király utca 80.", null, "BUDAPEST\nKirály utca 80.\n1068")]
    [InlineData("1068 Budapest Kodály Zoltán utca 97.", null, "BUDAPEST\nKodály Zoltán utca 97.\n1068")]
    [InlineData("Kovács Béla 1068 Budapest Király utca 80.", null, "Kovács Béla\nBUDAPEST\nKirály utca 80.\n1068")]
    [InlineData("1068 Budapest VI. ker. Király utca 80.", null, "BUDAPEST VI. KER.\nKirály utca 80.\n1068")]
    [InlineData("1300 Budapest Pf. 12.", "HU", "BUDAPEST\nPF. 12\n1300")]
    [InlineData("Postafiók 12., 1300 Budapest", null, "BUDAPEST\nPostafiók 12\n1300")]
    [InlineData("1300 Budapest, Postafiók 12", null, "BUDAPEST\nPostafiók 12\n1300")]
    [InlineData("Pf. 406, 4400 Nyíregyháza", "HU", "NYÍREGYHÁZA\nPF. 406\n4400")]
    public void HungarianPostcodeFirstAndBoxes(string input, string? hint, string postal) =>
        Assert.Equal(postal, Read(input, hint).ToString(AddressStyle.Postal));

    [Theory]
    [InlineData("1000 Brussel Kerkstraat 3")]
    [InlineData("4000 Basel Hauptstrasse 3")]
    [InlineData("1300 Budapest Pf. 12.")]
    [InlineData("1068 Budapest Király utca")]
    public void HungarianWithoutEvidenceIsNotGuessed(string input) =>
        Assert.True(AddressTestData.NamesNoCountry(input), input);

    // Romania: the label of the postcode is not part of the address.
    [Theory]
    [InlineData("Str. Mihai Eminescu nr. 5, Cluj-Napoca, cod poștal 400001", "Str. Mihai Eminescu nr. 5\n400001 CLUJ-NAPOCA")]
    [InlineData("Str. Mihai Eminescu nr. 5, Cluj-Napoca, Cod poștal: 400001", "Str. Mihai Eminescu nr. 5\n400001 CLUJ-NAPOCA")]
    [InlineData("Str. Mihai Eminescu nr. 5, Cluj-Napoca, cod 400001, România", "Str. Mihai Eminescu nr. 5\n400001 CLUJ-NAPOCA")]
    [InlineData("Str. Mihai Eminescu nr. 5, cod poștal 400001, Cluj-Napoca", "Str. Mihai Eminescu nr. 5\n400001 CLUJ-NAPOCA")]
    [InlineData("Str. Mihai Eminescu nr. 5, Cluj-Napoca, jud. Cluj, cod poștal 400001", "Str. Mihai Eminescu nr. 5\n400001 CLUJ-NAPOCA, JUD. CLUJ")]
    [InlineData("Str. Mihai Eminescu nr. 5\nCluj-Napoca\nCod: 400001", "Str. Mihai Eminescu nr. 5\n400001 CLUJ-NAPOCA")]
    public void RomanianPostcodeLabel(string input, string postal) =>
        Assert.Equal(postal, Read(input, "RO").ToString(AddressStyle.Postal));

    // A locality written on the street's line without a comma is the plain words after the last number.
    [Theory]
    [InlineData("Strada Frumoasă Nr. 51 București, Sector 1, 010986", "Strada Frumoasă Nr. 51\n010986 BUCUREȘTI SECTOR 1")]
    [InlineData("Str. Mihai Eminescu nr. 5 Cluj-Napoca, 400001", "Str. Mihai Eminescu nr. 5\n400001 CLUJ-NAPOCA")]
    [InlineData("Str. Mihai Eminescu nr. 5, Sector 2, București, cod poștal 020001", "Str. Mihai Eminescu nr. 5\nSector 2\n020001 BUCUREȘTI")]
    public void RomanianLocalityOnTheStreetLine(string input, string postal) =>
        Assert.Equal(postal, Read(input, "RO").ToString(AddressStyle.Postal));

    [Fact]
    public void RomanianBuildingTailIsNeverALocality() =>
        Assert.NotEqual(OperationStatus.Done, RuleAddressParser.TryParse("Str. Mihai Eminescu nr. 5 bl. A1, 400001", Region.Parse("RO"), out _, out _));

    [Fact]
    public void RomanianStreetNamedCodIsNotALabel()
    {
        // A word "cod" that is not followed by a postcode stays where it is.
        ParsedAddress a = Read("Str. Cod nr. 5, 400001 Cluj-Napoca", "RO");
        Assert.Equal("Str. Cod nr. 5\n400001 CLUJ-NAPOCA", a.ToString(AddressStyle.Postal));
    }

    // Australia: commas around the state and the postcode, and a unit before a slash number.
    [Theory]
    [InlineData("12 Smith Street, Sydney, NSW, 2000", "12 SMITH STREET\nSYDNEY NSW 2000")]
    [InlineData("12 Smith Street, Sydney NSW, 2000", "12 SMITH STREET\nSYDNEY NSW 2000")]
    [InlineData("12 Smith St, Surry Hills, NSW, 2010", "12 SMITH ST\nSURRY HILLS NSW 2010")]
    [InlineData("PO Box 123, Sydney, NSW, 2001", "PO BOX 123\nSYDNEY NSW 2001")]
    [InlineData("GPO Box 1234, Sydney, NSW, 2001", "GPO BOX 1234\nSYDNEY NSW 2001")]
    [InlineData("1/5 George Street, Hobart, TAS, 7000", "1/5 GEORGE STREET\nHOBART TAS 7000")]
    [InlineData("100 George Street, Sydney, NSW, 2000, Australia", "100 GEORGE STREET\nSYDNEY NSW 2000")]
    [InlineData("Unit 3/12 Smith Street, Sydney NSW 2000", "UNIT 3/12 SMITH STREET\nSYDNEY NSW 2000")]
    [InlineData("Shop 4/100 George Street, Sydney NSW 2000", "SHOP 4/100 GEORGE STREET\nSYDNEY NSW 2000")]
    [InlineData("Unit 5/20-22 George Street, North Sydney NSW 2060", "UNIT 5/20-22 GEORGE STREET\nNORTH SYDNEY NSW 2060")]
    public void AustralianCommaForms(string input, string postal) =>
        Assert.Equal(postal, Read(input, "AU").ToString(AddressStyle.Postal));

    [Theory]
    [InlineData("12 Smith Street, NSW, 2000")]
    [InlineData("Sydney, NSW, 2000")]
    [InlineData("12 Smith Street, Sydney NSW")]
    public void AustralianWithoutAStreetOrLocalityIsRefused(string input) =>
        Assert.NotEqual(OperationStatus.Done, RuleAddressParser.TryParse(input, Region.Parse("AU"), out _, out _));

    [Fact]
    public void AustralianUnitSlashKeepsItsParts()
    {
        ParsedAddress a = Read("Unit 3/12 Smith Street, Sydney NSW 2000", "AU");
        Assert.Equal("UNIT 3/12 SMITH STREET\nSYDNEY NSW 2000", a.ToString(AddressStyle.Postal));
        Assert.Contains(a.DeliveryLines[0].Parts, p => p.Role == StreetPartRole.UnitNumber && p.Standard == "3");
        Assert.Contains(a.DeliveryLines[0].Parts, p => p.Role == StreetPartRole.HouseNumber && p.Standard == "12");
    }
}
