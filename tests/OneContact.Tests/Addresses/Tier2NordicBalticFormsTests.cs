using System.Buffers;
using RecursiveAI.OneContact.Addresses;
using RecursiveAI.OneContact.Addresses.Parsing;

namespace RecursiveAI.OneContact.Tests.Addresses;

/// <summary>
/// The forms people write Nordic and Baltic addresses in on the web, and country inference for text that names no country (all inputs written for this repository), checked
/// against the posts' guides: Omniva (the postcode on the last line, the county after it), Posten Norge (Postboks with the box facility's name, Bolignummer), Danmarks Adresser
/// (floor, side and door), PostNord (lgh), the UPU Finland, Lithuania and Estonia sheets.
/// </summary>
public class Tier2NordicBalticFormsTests
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

    // Refused, or read with the country left unknown (every country that can read the text finds the same fields): no country is guessed.
    private static bool Refused(string text) => AddressTestData.NamesNoCountry(text);

    // Estonia: Omniva puts the postcode before the town; pages also put it after ("Tallinn 10145"), and the county may follow.
    [Theory]
    [InlineData("Pärnu mnt 12, Tallinn 10145", "street.name=Pärnu mnt ; street.house-number=12 ; locality=Tallinn ; postal-code=10145")]
    [InlineData("Riia tn 4 Tartu 51010", "street.name=Riia tn ; street.house-number=4 ; locality=Tartu ; postal-code=51010")]
    [InlineData("Kooli pst 2, Viljandi, 71020", "street.name=Kooli pst ; street.house-number=2 ; locality=Viljandi ; postal-code=71020")]
    [InlineData("Pärnu mnt 12\nTallinn 10145", "street.name=Pärnu mnt ; street.house-number=12 ; locality=Tallinn ; postal-code=10145")]
    [InlineData("Pärnu mnt 12, 10148 Tallinn", "street.name=Pärnu mnt ; street.house-number=12 ; postal-code=10148 ; locality=Tallinn")]
    [InlineData("Viru väljak 4, Tallinn 10111 Harju maakond", "street.name=Viru väljak ; street.house-number=4 ; locality=Tallinn ; postal-code=10111 ; admin-area=Harju maakond")]
    [InlineData("Aia tn 3, Tartu 51003 Tartumaa", "street.name=Aia tn ; street.house-number=3 ; locality=Tartu ; postal-code=51003 ; admin-area=Tartumaa")]
    [InlineData("Metsa tn 2-18, Põltsamaa vald, 48001 Adavere alevik Jõgeva maakond", "street.name=Metsa tn ; street.house-number=2 ; street.unit-number=18 ; dependent-locality=Põltsamaa vald ; postal-code=48001 ; locality=Adavere alevik ; admin-area=Jõgeva maakond")]
    [InlineData("Kodu küla, Rae vald, 75301 Harju maakond", "locality=Kodu küla ; dependent-locality=Rae vald ; postal-code=75301 ; admin-area=Harju maakond")]
    [InlineData("Pärnu mnt 12, 10148 Tallinn Harju maakond", "street.name=Pärnu mnt ; street.house-number=12 ; postal-code=10148 ; locality=Tallinn ; admin-area=Harju maakond")]
    public void EstonianPostcodeBeforeOrAfterTheTown(string input, string fields) => Assert.Equal(fields, Fields(Read(input, "EE")));

    // A trailing five-digit number is a ZIP code in the United States, so the Estonian reading needs the country.
    [Fact]
    public void EstonianPostcodeAfterTheTownIsNotReadAsAnAmericanZipWithEvidence()
    {
        ParsedAddress a = Read("Pärnu mnt 12, Tallinn 10145", null);
        Assert.Equal(Region.Parse("EE"), a.Country);
        Assert.True(a.CountryInferred);
    }

    // Norway: the box number is not the postcode, the post town follows the postcode (Posten Norge, Adressering).
    [Theory]
    [InlineData("Postboks 123 Majorstuen\n0349 Oslo", "street.box-designator=Postboks ; street.box-number=123 ; street.other=Majorstuen ; postal-code=0349 ; locality=Oslo")]
    [InlineData("Bedriften AS, Postboks 55 Falkum, 3705 Skien", "leading-line=Bedriften AS ; street.box-designator=Postboks ; street.box-number=55 ; street.other=Falkum ; postal-code=3705 ; locality=Skien")]
    [InlineData("Postboks 123 Majorstuen 0349 Oslo", "street.box-designator=Postboks ; street.box-number=123 ; street.other=Majorstuen ; postal-code=0349 ; locality=Oslo")]
    [InlineData("Storgata 15, inngang B, 0161 Oslo", "street.name=Storgata ; street.house-number=15 ; street.unit-designator=inngang ; street.unit-number=B ; postal-code=0161 ; locality=Oslo")]
    [InlineData("Storgata 15, 2. etasje, 0161 Oslo", "street.name=Storgata ; street.house-number=15 ; street.unit-number=2. etasje ; postal-code=0161 ; locality=Oslo")]
    [InlineData("Storgata 15 H0101, 0161 Oslo", "street.name=Storgata ; street.house-number=15 ; street.unit-number=H0101 ; postal-code=0161 ; locality=Oslo")]
    [InlineData("Storgata 15, 3. etasje, leil. H0301, 0161 Oslo", "street.name=Storgata ; street.house-number=15 ; street.unit-number=3. etasje ; street.unit-designator=leil. ; street.unit-number=H0301 ; postal-code=0161 ; locality=Oslo")]
    public void NorwegianBoxesAndUnits(string input, string fields) => Assert.Equal(fields, Fields(Read(input, "NO")));

    // Denmark: floor, side and door as Danmarks Adresser writes them ("3. sal tv", "st. mf"), and a building.
    [Theory]
    [InlineData("Nygårdsvænget 34, 3. sal tv, 2800 Kongens Lyngby", "street.name=Nygårdsvænget ; street.house-number=34 ; street.unit-number=3. sal tv ; postal-code=2800 ; locality=Kongens Lyngby")]
    [InlineData("Nørregade 10, st. mf, 1165 København K", "street.name=Nørregade ; street.house-number=10 ; street.unit-number=st. mf ; postal-code=1165 ; locality=København K")]
    [InlineData("Vestergade 12, 2. th, 8000 Aarhus C", "street.name=Vestergade ; street.house-number=12 ; street.unit-number=2. th ; postal-code=8000 ; locality=Aarhus C")]
    [InlineData("Havnegade 5 Bygning N, 1058 København K", "street.name=Havnegade ; street.house-number=5 ; street.unit-designator=Bygning ; street.unit-number=N ; postal-code=1058 ; locality=København K")]
    public void DanishFloorSideAndBuilding(string input, string fields) => Assert.Equal(fields, Fields(Read(input, "DK")));

    // Finland: kerros (krs), asunto (as.), staircase and apartment letters, and the post office box in Finnish and English.
    [Theory]
    [InlineData("Mannerheimintie 5, 3. krs, 00100 Helsinki", "street.name=Mannerheimintie ; street.house-number=5 ; street.unit-number=3. krs ; postal-code=00100 ; locality=Helsinki")]
    [InlineData("Mannerheimintie 5 3 krs, 00100 Helsinki", "street.name=Mannerheimintie ; street.house-number=5 ; street.unit-number=3 krs ; postal-code=00100 ; locality=Helsinki")]
    [InlineData("Mannerheimintie 5 as. 12, 00100 Helsinki", "street.name=Mannerheimintie ; street.house-number=5 ; street.unit-number=as. 12 ; postal-code=00100 ; locality=Helsinki")]
    [InlineData("Mannerheimintie 5 A 12, 00100 Helsinki", "street.name=Mannerheimintie ; street.house-number=5 A ; street.unit-number=12 ; postal-code=00100 ; locality=Helsinki")]
    [InlineData("PL 123, 00101 Helsinki", "street.box-designator=PL ; street.box-number=123 ; postal-code=00101 ; locality=Helsinki")]
    [InlineData("P.O. Box 123, 00101 Helsinki", "street.box-designator=P.O. Box ; street.box-number=123 ; postal-code=00101 ; locality=Helsinki")]
    public void FinnishFloorsApartmentsAndBoxes(string input, string fields) => Assert.Equal(fields, Fields(Read(input, "FI")));

    // Sweden: a company registration number is not a house number, a floor or an apartment.
    [Theory]
    [InlineData("Företaget AB, org.nr 556123-4567, Storgatan 5, 111 22 Stockholm", "leading-line=Företaget AB ; leading-line=org.nr 556123-4567 ; street.name=Storgatan ; street.house-number=5 ; postal-code=111 22 ; locality=Stockholm")]
    [InlineData("Storgatan 5, 556123-4567, 111 22 Stockholm", "street.name=Storgatan ; street.house-number=5 ; leading-line=556123-4567 ; postal-code=111 22 ; locality=Stockholm")]
    [InlineData("556123-4567 Storgatan 5 111 22 Stockholm", "leading-line=556123-4567 ; street.name=Storgatan ; street.house-number=5 ; postal-code=111 22 ; locality=Stockholm")]
    [InlineData("Storgatan 5 lgh 1102, 111 22 Stockholm", "street.name=Storgatan ; street.house-number=5 ; street.unit-designator=lgh ; street.unit-number=1102 ; postal-code=111 22 ; locality=Stockholm")]
    public void SwedishOrganizationNumberIsNotAHouseNumber(string input, string fields) => Assert.Equal(fields, Fields(Read(input, "SE")));

    // Lithuania: the country letters stand apart from the digits ("LT- 08237"); they belong to the postcode field.
    [Theory]
    [InlineData("Gedimino pr. 5, LT- 08237 Vilnius", "street.name=Gedimino pr. ; street.house-number=5 ; postal-code=LT- 08237 ; locality=Vilnius")]
    [InlineData("Ąžuolyno al. 3, LT- 08237 Vilnius", "street.name=Ąžuolyno al. ; street.house-number=3 ; postal-code=LT- 08237 ; locality=Vilnius")]
    [InlineData("Vilniaus g. 5, LT 08237 Vilnius", "street.name=Vilniaus g. ; street.house-number=5 ; postal-code=LT 08237 ; locality=Vilnius")]
    [InlineData("Vilniaus g. 5, LT-08237 Vilnius", "street.name=Vilniaus g. ; street.house-number=5 ; postal-code=LT-08237 ; locality=Vilnius")]
    public void LithuanianSpacedCountryPrefix(string input, string fields)
    {
        ParsedAddress a = Read(input, "LT");
        Assert.Equal(fields, Fields(a));
        Assert.Equal("08237", a.PostalCode!.Value.Standard);
    }

    // Country inference without a hint: the words and letters of the text decide when they belong to one country among those sharing the postcode form.
    [Theory]
    [InlineData("Storgata 15, 0161 Oslo", "NO")]
    [InlineData("Strandgata 5, 9008 Tromsø", "NO")]
    [InlineData("Nygårdsvænget 34, 2800 Kongens Lyngby", "DK")]
    [InlineData("Vestergade 12, 8000 Aarhus C", "DK")]
    [InlineData("Mannerheimintie 5, 00100 Helsinki", "FI")]
    [InlineData("Storgatan 5, 111 22 Stockholm", "SE")]
    [InlineData("Pärnu mnt 12, Tallinn 10145", "EE")]
    [InlineData("Pargi tn 24-4\n48303 Jõgeva Jõgeva maakond", "EE")]
    [InlineData("Gedimino pr. 5, LT- 08237 Vilnius", "LT")]
    [InlineData("Šaulių g. 4, 67132 Lazdijai", "LT")]
    [InlineData("Vijverstraat 17 1730 Asse", "BE")]
    [InlineData("Bučka 6, 8276 Bučka", "SI")]
    [InlineData("Menzelstr. 33\n48108 Hamm", "DE")]
    [InlineData("Okružná 10\n013 01 Teplička", "SK")]
    [InlineData("Churer Strasse 5, 9494 Schaan", "LI")]
    [InlineData("Viale Giuseppe Mazzini 5, 00195 Roma", "IT")]
    public void CountryInferredFromTheWordsOfTheText(string input, string country)
    {
        ParsedAddress a = Read(input, null);
        Assert.Equal(Region.Parse(country), a.Country);
        Assert.True(a.CountryInferred);
        Assert.True(a.CountryConfidence >= 0.97f);
    }

    // Truly ambiguous text is refused: the postcode shape and the words are shared by more than one country, or no word belongs to one.
    [Theory]
    [InlineData("Hauptstrasse 3, 4000 Basel")]
    [InlineData("Birkesti 8, 2791 Dragør")]
    [InlineData("Postboks 123 Majorstuen, 0349 Oslo")]
    [InlineData("Hartmanice 47, 373 65 Hartmanice")]
    [InlineData("Glasing 40 - 7540 Güssing")]
    [InlineData("Pyrkerstraße 47a, 5630 Bad Hofgastein")]
    [InlineData("Revelsvägen 108, 22340 Geta")]
    [InlineData("Rovte 89\n1373 Rovte")]
    [InlineData("Ekebo 5, 851 30 Sundsvall")]
    [InlineData("Kollárova 100/12, 900 21 Svätý Jur")]
    public void AmbiguousCountryIsRefused(string input) => Assert.True(Refused(input), input);

    // A letter or word of another language vetoes a reading: Czech letters are not Slovak, a Slovak postcode zone is not Czech.
    [Theory]
    [InlineData("Dlouhá 12, 013 01 Žilina", "SK")]
    public void SlovakPostcodeZonesAndLetters(string input, string country) => Assert.Equal(Region.Parse(country), Read(input, null).Country);

    [Fact]
    public void ChecksAgainstReadingsOfTheWrongCountry()
    {
        // "4261 BK" is a Dutch postcode, not a Belgian town; "BZ" is the Italian province code, not part of a German locality; a country name at the end names the country.
        foreach (string input in new[] { "Aalburgsestraat 26 4261 BK", "Brennerstrasse 8, 39100 Bozen BZ", "Závodie P.O.BOX 1970 010 04 Žilina Slovenská republika", "Kirchbichlstrasse 16, 39030 Vintl (bz)" })
        {
            OperationStatus status = RuleAddressParser.TryParse(input, Region.Unknown, out ParsedAddress? a, out _);
            Assert.True(status != OperationStatus.Done || a!.Country.Code is "NL" or "IT" or "SK", input);
        }
    }

    // American addresses keep being American: the trial readings do not take a ZIP code last.
    [Theory]
    [InlineData("123 Main St, Springfield, IL 62704")]
    [InlineData("1600 Pennsylvania Ave NW, Washington, DC 20500")]
    [InlineData("Calle Luna 5, San Juan, PR 00901")]
    public void AmericanAddressesAreNotStolen(string input) => Assert.Equal(Region.US, Read(input, null).Country);
}
