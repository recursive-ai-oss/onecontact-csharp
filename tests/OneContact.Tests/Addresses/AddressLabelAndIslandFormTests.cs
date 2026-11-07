using System.Buffers;
using RecursiveAI.OneContact.Addresses.Parsing;

namespace RecursiveAI.OneContact.Tests.Addresses;

/// <summary>Forms seen on Hungarian, Polish and Isle of Man pages: a label before the address, and "United Kingdom" after an island's postcode.</summary>
public class AddressLabelAndIslandFormTests
{
    [Theory]
    [InlineData("Cím: 1053 Budapest, Kossuth Lajos utca 5.", "HU")]
    [InlineData("Székhely: 1053 Budapest, Kossuth Lajos utca 5.", "HU")]
    [InlineData("Levelezési cím: Kossuth Lajos utca 5., 1053 Budapest", "HU")]
    [InlineData("Cím:\n1053 Budapest, Kossuth Lajos utca 5.", "HU")]
    public void ALabelBeforeTheAddressIsNotPartOfAnyField(string text, string country)
    {
        Assert.Equal(OperationStatus.Done, RuleAddressParser.TryParse(text, Region.Parse(country), out ParsedAddress? a, out _));
        Assert.Equal("Budapest", a!.Locality!.Value.Text);
        Assert.Equal("1053", a.PostalCode!.Value.Text);
        Assert.True(a.Span.Start > text.IndexOf(':', StringComparison.Ordinal));
        Assert.DoesNotContain(a.Fields, f => f.Text.Contains(':', StringComparison.Ordinal));
        Assert.Contains(a.Fields, f => f.Name == "street.name" && f.Text == "Kossuth Lajos");
    }

    [Fact]
    public void UnitedKingdomAfterAnIslandPostcodeKeepsTheIslandAndTheWrittenName()
    {
        Assert.Equal(OperationStatus.Done, RuleAddressParser.TryParse("12 Athol Street, Douglas, IM1 1AA, United Kingdom", Region.Parse("IM"), out ParsedAddress? a, out ParseDetail detail));
        Assert.Equal(Region.Parse("IM"), a!.Country);
        Assert.Equal("United Kingdom", a.CountryName!.Value.Text);
        Assert.Equal("UNITED KINGDOM", a.CountryName.Value.Standard);
        Assert.Contains(detail.Issues, i => i.Reason.Equals(ReasonCode.AddressCountryConflict) && i.Severity == IssueSeverity.Warning);

        // Without the hint the island's postcode still cannot be called Britain's: the conflict stays a refusal.
        Assert.Equal(OperationStatus.InvalidData, RuleAddressParser.TryParse("12 Athol Street, Douglas, IM1 1AA, United Kingdom", Region.Unknown, out _, out _));
    }

    [Fact]
    public void AnIslandNameAfterATownWithoutAStreetLineIsTheCountry()
    {
        Assert.Equal(OperationStatus.Done, RuleAddressParser.TryParse("Ballasalla, Isle of Man, IM9 2AA", Region.Parse("IM"), out ParsedAddress? a, out _));
        Assert.Equal("Ballasalla", a!.Locality!.Value.Text);
        Assert.Equal("Isle of Man", a.CountryName!.Value.Text);
    }
}
