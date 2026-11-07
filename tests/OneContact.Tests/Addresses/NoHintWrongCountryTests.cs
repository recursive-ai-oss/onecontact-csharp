using System.Buffers;
using RecursiveAI.OneContact.Addresses.Parsing;

namespace RecursiveAI.OneContact.Tests.Addresses;

/// <summary>
/// Without a hint the rules never read a text in a guessed country: what does not show the country is refused or returned without one. Each case is written for this
/// test; the shapes are the ones real pages produced.
/// </summary>
public class NoHintWrongCountryTests
{
    private static (OperationStatus Status, ParsedAddress? Address) Read(string text, string hint = "")
    {
        OperationStatus s = RuleAddressParser.TryParse(text, hint.Length == 0 ? Region.Unknown : Region.Parse(hint), out ParsedAddress? a, out _);
        return (s, a);
    }

    private static void AssertNotRead(string text)
    {
        (OperationStatus status, ParsedAddress? a) = Read(text);
        Assert.True(status != OperationStatus.Done || a is { Country.IsUnknown: true }, $"'{text}' was read as {a?.Country}");
    }

    // A reading in Mexico (or no reading) is fine; one in the United States or Canada is a wrong country.
    private static void AssertNotUsOrCanada(string text)
    {
        (OperationStatus status, ParsedAddress? a) = Read(text);
        Assert.True(status != OperationStatus.Done || a!.Country != Region.US && a.Country != Region.CA, $"'{text}' was read as {a?.Country}");
    }

    [Theory]
    [InlineData("1 Main Street, Springfield 22162")]
    [InlineData("Pärnu tn 1 Paide, Järvamaa 72711")]
    [InlineData("Parko str. 1, Plunge 90100")]
    [InlineData("Benito Juárez 100, Centro, Santa Catarina, Nuevo León, CP 66100")]
    public void AFiveDigitNumberWithoutAStateIsNotAZip(string text) => AssertNotUsOrCanada(text);

    [Theory]
    [InlineData("Av. Constitución 100, Centro, 64000 Monterrey, N.L.")]
    [InlineData("Calle Hidalgo 12, Centro, 22014 Tijuana, B.C.")]
    [InlineData("Valle Dorado S/N, Colonia Loma Linda, 21140 Mexicali, Baja California")]
    public void MexicanStatesAreNotAmericanOrCanadian(string text) => AssertNotUsOrCanada(text);

    [Fact]
    public void StatesProvincesAndPostalCodesStillShowTheCountry()
    {
        Assert.Equal(Region.US, Read("1 Main Street, Springfield, VA 22162").Address!.Country);
        Assert.Equal(Region.US, Read("1 Main Street, Springfield 22162, USA").Address!.Country);
        Assert.Equal(Region.CA, Read("29 Deer Park Way, Spruce Grove, Alberta").Address!.Country);
        Assert.Equal(Region.CA, Read("45 Oak Avenue, Vancouver, BC V6B 1A1").Address!.Country);
        Assert.Equal(Region.US, Read("1 Main Street, Springfield 22162", "US").Address!.Country);
    }

    [Fact]
    public void ACanadianPostalCodeOutweighsAFiveDigitHouseNumber()
    {
        (OperationStatus status, ParsedAddress? a) = Read("10000 Kingsway Ave, Edmonton, AB T5G 3J5");
        Assert.True(status != OperationStatus.Done || a!.Country == Region.CA);
    }

    [Theory]
    [InlineData("Musterweg 4, 97478 Knetzgau")]
    [InlineData("Muster Werbung GmbH, Seeweg 4, 97478 Knetzgau")]
    [InlineData("Calle 60 100 Fracc. Floresta, C.P. 97306 Mérida Yucatán")]
    public void OverseasRangePostcodeWithAGermanOrMexicanCueIsNotFrench(string text) => AssertNotRead(text);

    [Fact]
    public void OverseasRangePostcodeWithoutACueIsStillFrench() =>
        Assert.Equal(Region.Parse("FR"), Read("12 rue de la Paix, 97400 Saint-Denis").Address!.Country);

    [Fact]
    public void ARomanceArticleAfterAPostcodeIsTheLocalityNotDutchLetters()
    {
        AssertNotRead("Chemin du Moulin 1\n6980 LA ROCHE EN ARDENNE");
        Assert.Equal(Region.Parse("NL"), Read("Dorpsstraat 12, 1234 AB Amsterdam").Address!.Country);
        Assert.Equal(Region.Parse("NL"), Read("Adrianastraat 34\n6832 AM Arnhem").Address!.Country);
    }
}
