using System.Buffers;
using RecursiveAI.OneContact.Addresses.Parsing;

namespace RecursiveAI.OneContact.Tests.Addresses;

/// <summary>
/// A postcode that also exists in the hinted country is read in that country. 97xxx is the French overseas departments' range, and also Würzburg and
/// Yucatán; the hint settles which, so a hinted German or Mexican address is never returned as French.
/// </summary>
public class HintedPostcodeRangeTests
{
    [Theory]
    [InlineData("Musterweg 4, 97478 Knetzgau", "DE")]
    [InlineData("Hauptstraße 10, 98527 Suhl", "DE")]
    [InlineData("Calle 60 100 Fracc. Floresta, C.P. 97306 Mérida, Yucatán", "MX")]
    public void HintDecidesWhenThePostcodeExistsThere(string text, string hint)
    {
        Assert.Equal(OperationStatus.Done, RuleAddressParser.TryParse(text, Region.Parse(hint), out ParsedAddress? a, out _));
        Assert.Equal(Region.Parse(hint), a!.Country);
    }

    [Fact]
    public void WithoutAHintAnOverseasPostcodeIsFrench()
    {
        Assert.Equal(OperationStatus.Done, RuleAddressParser.TryParse("12 rue de la Paix, 97400 Saint-Denis", Region.Unknown, out ParsedAddress? a, out _));
        Assert.True(a!.Country.ToString() is "FR" or "RE");
    }
}
