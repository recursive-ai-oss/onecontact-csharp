using System.Buffers;
using RecursiveAI.OneContact.Addresses;
using RecursiveAI.OneContact.Addresses.Parsing;
using RecursiveAI.OneContact.Addresses.Standardization;

namespace RecursiveAI.OneContact.Tests.Addresses;

/// <summary>
/// Forms of Lithuanian, Mexican, Polish, Slovenian and Slovak addresses the rules read since the recovery round: each input is written for the test, in the form the post's guide
/// or the country's usage shows (Poczta Polska's flat words, the UPU Mexico sheet's colonia line and "C.P.", the UPU Lithuania sheet's rural address).
/// </summary>
public class Tier2RuleRecoveryTests
{
    private static ParsedAddress Parse(string text, string? hint)
    {
        OperationStatus status = RuleAddressParser.TryParse(text, hint is null ? Region.Unknown : Region.Parse(hint), out ParsedAddress? a, out ParseDetail d);
        Assert.True(status == OperationStatus.Done, $"{text}: not read: {d.PrimaryReason}");
        return a!;
    }

    private static string? Field(ParsedAddress a, string name) => a.Fields.FirstOrDefault(f => f.Name == name).Text;

    private static string Fields(ParsedAddress a, string name) => string.Join("|", a.Fields.Where(f => f.Name == name).Select(f => f.Text));

    private static void AssertFieldsPointIntoTheInput(ParsedAddress a, string input)
    {
        foreach (ParsedAddressField f in a.Fields)
        {
            Assert.Equal(f.Text, input.Substring(f.Span.Start, f.Span.Length));
        }
    }

    // ---- Poland ----------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void PolishCodeWithoutItsDashIsReadOnlyForPolishText()
    {
        const string input = "ul. Wolności 25, 08530 Dęblin";
        ParsedAddress a = Parse(input, "PL");
        Assert.Equal("08530", Field(a, "postal-code"));
        Assert.Equal("08-530", a.PostalCode!.Value.Standard);
        Assert.Equal("Dęblin", Field(a, "locality"));
        AssertFieldsPointIntoTheInput(a, input);

        // Five digits are also French, German and others': without a hint or a country line the text is never read as Polish.
        if (RuleAddressParser.TryParse(input, Region.Unknown, out ParsedAddress? unhinted, out _) == OperationStatus.Done)
        {
            Assert.NotEqual(Region.Parse("PL"), unhinted!.Country);
        }
    }

    [Theory]
    [InlineData("ul. Kwiatowa 2/116, 58-210 Łagiewniki", "2", "116")]
    [InlineData("ul. Zuchów 20A/84 82-500 Kwidzyn", "20A", "84")]
    public void PolishBuildingAndFlatAreTwoNumbersPrintedAsOne(string input, string house, string unit)
    {
        // Poczta Polska, Zasady poprawnego adresowania 1.2: the building number and, after it, the flat.
        ParsedAddress a = Parse(input, "PL");
        Assert.Equal(house, Field(a, "street.house-number"));
        Assert.Equal(unit, Field(a, "street.unit-number"));
        Assert.Contains(house + "/" + unit, a.ToString(AddressStyle.Postal));
        AssertFieldsPointIntoTheInput(a, input);
    }

    [Theory]
    [InlineData("Janina Nowak\nul. Lipowa 6 m. 80\n86-022 Dobrcz")]
    [InlineData("Janina Nowak, ul. Lipowa, 6 m. 80, 86-022 Dobrcz")]
    [InlineData("Janina Nowak, ul. Lipowa 6, m. 80, 86-022 Dobrcz")]
    public void PolishFlatWordIsTheDesignatorAndItsNumberTheUnit(string input)
    {
        ParsedAddress a = Parse(input, "PL");
        Assert.Equal("m.", Field(a, "street.unit-designator"));
        Assert.Equal("80", Field(a, "street.unit-number"));
        Assert.Equal("6", Field(a, "street.house-number"));
        Assert.Equal("Lipowa", Field(a, "street.name"));
        AssertFieldsPointIntoTheInput(a, input);
    }

    [Fact]
    public void PolishPremisesWordIsADesignatorToo()
    {
        ParsedAddress a = Parse("Biuro Alfa\nul. Beskidzka 8 lok. 5\n58-307 Wałbrzych", "PL");
        Assert.Equal("lok.", Field(a, "street.unit-designator"));
        Assert.Equal("5", Field(a, "street.unit-number"));
        Assert.Contains("ul. Beskidzka 8 lok. 5", a.ToString(AddressStyle.Postal));
    }

    [Fact]
    public void PolishShortBoxWordStartsABoxLine()
    {
        ParsedAddress a = Parse("Skrytka 396 / 36-004 Łąka", "PL");
        Assert.Equal("Skrytka", Field(a, "street.box-designator"));
        Assert.Equal("396", Field(a, "street.box-number"));
    }

    // ---- Slovenia and Slovakia -------------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Pondor 19 a\nSI-3304 Tabor", "19 a")]
    [InlineData("Boreci 4 c, 9242 Križevci pri Ljutomeru", "4 c")]
    [InlineData("Pondor 19a, 3304 Tabor", "19a")]
    public void SlovenianHouseNumberKeepsItsLetter(string input, string house)
    {
        ParsedAddress a = Parse(input, "SI");
        Assert.Equal(house, Field(a, "street.house-number"));
        Assert.Null(Field(a, "street.unit-designator"));
        AssertFieldsPointIntoTheInput(a, input);
    }

    [Fact]
    public void SlovenianBoxIsPPWithPeriods()
    {
        // UPU Slovenia sheet: "p.p." and a number.
        ParsedAddress a = Parse("Podjetje d.o.o.\np. p. 3795\n1000 Ljubljana", "SI");
        Assert.Equal("p. p.", Field(a, "street.box-designator"));
        Assert.Equal("3795", Field(a, "street.box-number"));
    }

    [Theory]
    [InlineData("P.O.BOX 341 / 956 05 Vozokany", "P.O.BOX", "341")]
    [InlineData("Poštový priečinok 1084, 086 04 Gerlachov", "Poštový priečinok", "1084")]
    public void SlovakBoxWordsStartABoxLine(string input, string designator, string number)
    {
        // UPU Slovakia sheet: P.O. Box; the Slovak words are the post's own.
        ParsedAddress a = Parse(input, "SK");
        Assert.Equal(designator, Field(a, "street.box-designator"));
        Assert.Equal(number, Field(a, "street.box-number"));
        Assert.Null(Field(a, "street.house-number"));
    }

    [Fact]
    public void ABoxWordInsideAStreetLineIsLeftToTheModel()
    {
        Assert.Equal(OperationStatus.InvalidData, RuleAddressParser.TryParse("Závodie P.O.BOX 1970 010 04 Žilina", Region.Parse("SK"), out _, out ParseDetail d));
        Assert.Equal(ReasonCode.AddressModelRequired, d.PrimaryReason);
    }

    [Fact]
    public void LegalFormAfterACommaBelongsToTheCompanyName()
    {
        const string input = "Mikuš a spol., v.o.s., Neded 931, 925 85 Neded";
        ParsedAddress a = Parse(input, "SK");
        Assert.Equal("Mikuš a spol., v.o.s.", Field(a, "leading-line"));
        Assert.Equal("Neded", Field(a, "street.name"));
        AssertFieldsPointIntoTheInput(a, input);
    }

    // ---- Mexico ----------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void MexicanPostcodeWordIsPartOfThePostcodeField()
    {
        const string input = "Calle Marfik 114\nFraccionamiento El Lucero II\nC.P. 37240, León, Guanajuato";
        ParsedAddress a = Parse(input, "MX");
        Assert.Equal("C.P. 37240", Field(a, "postal-code"));
        Assert.Equal("37240", a.PostalCode!.Value.Standard);
        Assert.Equal("León", Field(a, "locality"));
        Assert.Equal("Guanajuato", Field(a, "admin-area"));
        Assert.Equal("Fraccionamiento El Lucero II", Field(a, "dependent-locality"));
        AssertFieldsPointIntoTheInput(a, input);
        Assert.EndsWith("37240 LEÓN, GTO", a.ToString(AddressStyle.Postal));
    }

    [Theory]
    [InlineData("CLAVEL S/N, COL SANTA CECILIA, LA BARCA, JALISCO, C.P. 47912", "LA BARCA", "JALISCO", "COL SANTA CECILIA")]
    [InlineData("C. Leandro Valle #303, Col. Centro, C.P. 38500, Apaseo el Alto, Guanajuato", "Apaseo el Alto", "Guanajuato", "Col. Centro")]
    [InlineData("Calle Allende 12\nCol. Centro\nPénjamo, C.P. 36900", "Pénjamo", null, "Col. Centro")]
    public void MexicanCodeAfterTheLocalityOrBeforeItIsRead(string input, string locality, string? state, string colonia)
    {
        ParsedAddress a = Parse(input, "MX");
        Assert.Equal(locality, Field(a, "locality"));
        Assert.Equal(state, Field(a, "admin-area"));
        Assert.Equal(colonia, Field(a, "dependent-locality"));
        Assert.StartsWith("C.P. ", Field(a, "postal-code"));
        AssertFieldsPointIntoTheInput(a, input);
    }

    [Fact]
    public void MexicanCodeWordIsNotEnoughWithoutAHintForSpanishLookingText()
    {
        // "C.P." is written in Spain too: without a hint, a colonia or a Mexican state the country stays open.
        Assert.NotEqual(OperationStatus.Done, RuleAddressParser.TryParse("Calle Mayor 5, C.P. 28013 Madrid", Region.Unknown, out _, out _));
        ParsedAddress a = Parse("Calle Mayor 5, Col. Centro, C.P. 28013, Toluca, Estado de México", null);
        Assert.Equal(Region.Parse("MX"), a.Country);
    }

    [Fact]
    public void MexicanStreetWithoutANumberEndsInSN()
    {
        ParsedAddress a = Parse("Calle 28 S/N\nCol. Centro\n40330 Buenavista de Cuéllar, Guerrero", "MX");
        Assert.Equal("28", Field(a, "street.name"));
        Assert.Equal("S/N", Field(a, "street.house-number"));
        Assert.Equal("Col. Centro", Field(a, "dependent-locality"));
    }

    [Theory]
    [InlineData("Calle Feliciano Cobián 105B Col. Los Angeles 27140 Torreón, Coahuila de Zaragoza", "Col. Los Angeles", "Torreón", "Coahuila de Zaragoza")]
    [InlineData("Av. Rio Papaloapan 405 Colonia Issste C.P. 42083, Pachuca de Soto, Hidalgo", "Colonia Issste", "Pachuca de Soto", "Hidalgo")]
    [InlineData("Calle Cuauhtémoc 34, Barrio San Juan, 54467 Nicolás Romero, Estado de México", "Barrio San Juan", "Nicolás Romero", "Estado de México")]
    public void MexicanColoniaOnTheStreetLineIsALineOfItsOwn(string input, string colonia, string locality, string state)
    {
        ParsedAddress a = Parse(input, "MX");
        Assert.Equal(colonia, Field(a, "dependent-locality"));
        Assert.Equal(locality, Field(a, "locality"));
        Assert.Equal(state, Field(a, "admin-area"));
    }

    [Fact]
    public void MexicanTownNamedAfterAStateIsNotSplit()
    {
        ParsedAddress a = Parse("C. Ramon Corona 275A, 47610 Tepatitlán de Morelos", "MX");
        Assert.Equal("Tepatitlán de Morelos", Field(a, "locality"));
        Assert.Null(Field(a, "admin-area"));
    }

    [Fact]
    public void CiudadDeMexicoIsTheStateNotTheCountry()
    {
        ParsedAddress a = Parse("Av. Reforma 10\nCol. Juárez\nC.P. 06600, Cuauhtémoc, Ciudad de México", "MX");
        Assert.Equal("Ciudad de México", Field(a, "admin-area"));
        Assert.Null(Field(a, "country"));
    }

    [Fact]
    public void MexicoOnItsOwnLineLeavesTheLocalityLinesMexicoTheState()
    {
        ParsedAddress a = Parse("Av. Reforma 10\n53490 Naucalpan de Juárez, México\nMéxico", "MX");
        Assert.Equal("México", Fields(a, "admin-area"));
        Assert.Equal("México", Fields(a, "country"));
        Assert.Equal("Naucalpan de Juárez", Field(a, "locality"));
    }

    [Fact]
    public void AmbiguousMexicoBeforeTheCodeIsLeftToTheModel()
    {
        // México is the state or the country; with nothing else to tell, the rules do not choose.
        Assert.NotEqual(OperationStatus.Done, RuleAddressParser.TryParse("Calle Villa S/N, Ecatepec de Morelos, México, C.P. 55418", Region.Parse("MX"), out _, out _));
    }

    [Fact]
    public void MexicanLegalFormAfterACommaBelongsToTheCompanyName()
    {
        const string input = "Servicios Díaz, S.C., Calle Cerro 10, Col. Lomas, 22680 Tijuana, Baja California";
        ParsedAddress a = Parse(input, "MX");
        Assert.Equal("Servicios Díaz, S.C.", Field(a, "leading-line"));
        AssertFieldsPointIntoTheInput(a, input);
    }

    // ---- Lithuania -------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void LithuanianVillageAndMunicipalityLines()
    {
        const string input = "Tvenkinio g. 4\nPienionių k., Anykščių r. sav.\nLT-29280 Pienionys";
        ParsedAddress a = Parse(input, "LT");
        Assert.Equal("Pienionių k.", Field(a, "dependent-locality"));
        Assert.Equal("Anykščių r. sav.", Field(a, "admin-area"));
        Assert.Equal("LT-29280", Field(a, "postal-code"));
        Assert.Equal("Pienionys", Field(a, "locality"));
        Assert.Equal("Anykščių r. sav.", a.AdminArea!.Value.Text);
        AssertFieldsPointIntoTheInput(a, input);
    }

    [Theory]
    [InlineData("Margirio g. 37, Punios k., Alytaus r. sav., LT-64401 Punia", "Punios k.", "Alytaus r. sav.")]
    [InlineData("Vindžiulių g. 22A / Vindžiulių k., Elektrėnų sav. / LT-21406 Vindžiuliai", "Vindžiulių k.", "Elektrėnų sav.")]
    [InlineData("Miško a. 7 Darsūniškio k. Radviliškio r. sav. LT-82182 Radviliškis", "Darsūniškio k.", "Radviliškio r. sav.")]
    [InlineData("Tvenkinio g. 17, Dabužių I k., Alytaus m. sav., LT-29271 Dabužiai I", "Dabužių I k.", "Alytaus m. sav.")]
    public void LithuanianVillageAndMunicipalityOnOneLine(string input, string village, string municipality)
    {
        ParsedAddress a = Parse(input, "LT");
        Assert.Equal(village, Field(a, "dependent-locality"));
        Assert.Equal(municipality, Field(a, "admin-area"));
        AssertFieldsPointIntoTheInput(a, input);
    }

    [Fact]
    public void LithuanianVillageAloneIsNotAHouseName()
    {
        ParsedAddress a = Parse("Gaidės g. 55\nJuliškės k.\n31151 Juliškė", "LT");
        Assert.Equal("Juliškės k.", Field(a, "dependent-locality"));
        Assert.Equal("Juliškė", Field(a, "locality"));
    }

    [Theory]
    [InlineData("Ramybės g. 25 - PD 2876 - LT-69307 Menkupiai", "PD", "2876")]
    [InlineData("Ramybės g. 25\nPašto dėžutė 12\nLT-69307 Menkupiai", "Pašto dėžutė", "12")]
    public void LithuanianBoxWords(string input, string designator, string number)
    {
        ParsedAddress a = Parse(input, "LT");
        Assert.Equal(designator, Field(a, "street.box-designator"));
        Assert.Equal(number, Field(a, "street.box-number"));
    }

    [Theory]
    [InlineData("K. Paulauskas\nB. Sruogos g. 9, butas 1\nLT-59209 Birštonas", "butas", "1")]
    [InlineData("K. Paulauskas, B. Sruogos g. 9, butas 1, LT-59209 Birštonas", "butas", "1")]
    [InlineData("K. Paulauskas\nB. Sruogos g. 9, b. 3\nLT-59209 Birštonas", "b.", "3")]
    [InlineData("K. Paulauskas, B. Sruogos g. 9, b. 3, LT-59209 Birštonas", "b.", "3")]
    public void LithuanianApartmentWords(string input, string designator, string number)
    {
        ParsedAddress a = Parse(input, "LT");
        Assert.Equal("9", Field(a, "street.house-number"));
        Assert.Equal(designator, Field(a, "street.unit-designator"));
        Assert.Equal(number, Field(a, "street.unit-number"));
    }

    [Fact]
    public void LithuanianSlashSeparatesHouseAndApartment()
    {
        ParsedAddress a = Parse("Debreceno g. 66/72\nLT-94152 Klaipėda", "LT");
        Assert.Equal("66", Field(a, "street.house-number"));
        Assert.Equal("72", Field(a, "street.unit-number"));
        Assert.Contains("Debreceno g. 66/72", a.ToString(AddressStyle.Postal));
    }

    [Fact]
    public void LithuanianLegalFormAfterACommaBelongsToTheCompanyName()
    {
        const string input = "Sakalauskas ir Ko, MB, Aušros g. 23, LT-69220 Kalvarija";
        ParsedAddress a = Parse(input, "LT");
        Assert.Equal("Sakalauskas ir Ko, MB", Field(a, "leading-line"));
        Assert.Equal("Aušros g.", Field(a, "street.name"));
        AssertFieldsPointIntoTheInput(a, input);
    }
}
