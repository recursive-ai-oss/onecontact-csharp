using RecursiveAI.OneContact.Addresses;
using RecursiveAI.OneContact.Addresses.Standardization;
using Xunit;

namespace RecursiveAI.OneContact.Tests.Addresses;

/// <summary>The happy-path addresses the AOT smoke app parses without a hint: each must parse, with the country inferred.</summary>
public sealed class SampleAddressRegressionTests
{
    [Theory]
    [InlineData("Calle Huertas 18, 4º, C\n29400 Marbella\nMálaga\nSpain", "ES", "Calle Huertas 18, 4º, C\n29400 MARBELLA\nMÁLAGA\nSPAIN")]
    [InlineData("10B Barry Jackson Tower, Estone Walk, Birmingham B6 5BA", "GB", "10B Barry Jackson Tower\nEstone Walk\nBIRMINGHAM\nB6 5BA")]
    [InlineData("Stavbařů 4211\n190 16 PRAHA 916\nCzech Republic", "CZ", "Stavbařů 4211\n190 16  PRAHA 916\nCZECH REPUBLIC")]
    public void SmokeAddressesParseAndRender(string input, string country, string expected)
    {
        PostalAddress a = PostalAddress.Parse(input);
        Assert.Equal(country, a.Country.Code);
        Assert.Equal(expected, a.ToString(AddressStyle.Postal, AddressLayout.Label, includeCountry: country != "GB"));
    }
}
