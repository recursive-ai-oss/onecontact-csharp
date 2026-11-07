namespace RecursiveAI.OneContact.Tests;

// Inputs found by the coverage-guided fuzz runs (fuzz/README.md). Each must be read or refused, never throw.
public class FuzzRegressionTests
{
    // Free-text address reading without a country: a hyphenated postcode followed by more numbers made a token span with a negative length.
    [Theory]
    [InlineData("393rd st1, executie 296606-2783,2783, 21, nxford, tx 12982")]
    [InlineData("393rd st\uFFFD #221, exeWutie 296606-2783,21, nxford, tx 12982")]
    public void AddressWithHyphenatedPostcodeAndTrailingNumbersDoesNotThrow(string text)
    {
        if (PostalAddress.TryParse(text, out PostalAddress address))
        {
            _ = address.Validate();
            _ = address.GetLabelLines();
        }
    }
}
