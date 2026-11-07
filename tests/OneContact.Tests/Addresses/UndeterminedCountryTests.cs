using System.Buffers;
using System.Text.Json;
using RecursiveAI.OneContact.Addresses;
using RecursiveAI.OneContact.Addresses.Parsing;
using RecursiveAI.OneContact.Addresses.Standardization;

namespace RecursiveAI.OneContact.Tests.Addresses;

/// <summary>
/// Text whose postcode form is shared by several countries and holds nothing that names one: when every country that can read it finds the same fields, the fields are
/// returned with the country left unknown; when they disagree, the text is refused as before.
/// </summary>
public class UndeterminedCountryTests
{
    // "Somewhere 12, 1234 Anywhere": four digits (Belgium, Austria, Switzerland, Denmark, Norway and others read it alike), no word or letter of any one language.
    private const string Shared = "Somewhere 12, 1234 Anywhere";

    [Fact]
    public void SameFieldsInEveryCandidateCountryAreReturnedWithoutACountry()
    {
        Assert.Equal(OperationStatus.Done, RuleAddressParser.TryParse(Shared, Region.Unknown, out ParsedAddress? address, out ParseDetail detail));
        Assert.NotNull(address);
        Assert.True(address.Country.IsUnknown);
        Assert.False(address.CountryInferred);
        Assert.Equal(0f, address.CountryConfidence);
        Assert.True(address.Facts.HasFlag(AddressFacts.CountryUndetermined));
        Assert.Equal("1234", address.PostalCode!.Value.Text);
        Assert.Equal("Anywhere", address.Locality!.Value.Text);
        Assert.Contains(address.Fields, f => f.Name == "street.house-number" && f.Text == "12");

        // The reason is a warning on a successful read, with every field still carrying its offsets and a rule as its source.
        Assert.Contains(detail.Issues, i => i.Reason.Equals(ReasonCode.AddressCountryUndetermined) && i.Severity == IssueSeverity.Warning);
        Assert.All(detail.Fields, f => Assert.Equal(DecisionSource.Rule, f.Source));
    }

    [Fact]
    public void CandidatesThatDisagreeAreRefused()
    {
        // A French house number can follow the street name or lead it, and the countries that share the form read the line differently: no country, no fields.
        Assert.Equal(OperationStatus.InvalidData, RuleAddressParser.TryParse("Rue de la Gare 3, 1000 Lausanne", Region.Unknown, out _, out ParseDetail detail));
        Assert.True(detail.PrimaryReason.Equals(ReasonCode.AddressCountryUndetermined));
    }

    [Fact]
    public void AHintOrEvidenceStillDecidesTheCountry()
    {
        Assert.Equal(OperationStatus.Done, RuleAddressParser.TryParse(Shared, Region.Parse("BE"), out ParsedAddress? hinted, out _));
        Assert.Equal(Region.Parse("BE"), hinted!.Country);
        Assert.False(hinted.Facts.HasFlag(AddressFacts.CountryUndetermined));

        Assert.Equal(OperationStatus.Done, RuleAddressParser.TryParse("Storgata 5, 0155 Oslo", Region.Unknown, out ParsedAddress? inferred, out _));
        Assert.Equal(Region.Parse("NO"), inferred!.Country);
        Assert.True(inferred.CountryInferred);
    }

    [Fact]
    public void ValidateReportsTheMissingCountry()
    {
        PostalAddress address = PostalAddress.Parse(Shared);
        Assert.True(address.Country.IsUnknown);
        Assert.True(address.Flags.HasFlag(AddressFacts.CountryUndetermined));
        Assert.Contains(address.Validate(), i => i.Field == AddressField.Country && i.Kind == AddressIssueKind.MissingRequiredField);
        Assert.Equal(AddressCoverage.Structural, address.Coverage);
    }

    [Fact]
    public void RenderingsKeepTheTextAsWritten()
    {
        PostalAddress address = PostalAddress.Parse("SOMEWHERE 12\n1234 anywhere");
        Assert.Equal("SOMEWHERE 12\n1234 anywhere", address.ToString(AddressStyle.Postal, AddressLayout.Label));
        Assert.Equal("SOMEWHERE 12\n1234 anywhere", address.ToString(AddressStyle.Display, AddressLayout.Label, includeCountry: true));
        Assert.Equal("SOMEWHERE 12, 1234 anywhere", address.ToString(AddressStyle.Postal, AddressLayout.SingleLine));
        Assert.Equal("SOMEWHERE 12, 1234 anywhere", address.Canonical);
    }

    [Fact]
    public void KeyIsMadeFromTheFieldsUnderTheUnknownCountryPrefix()
    {
        PostalAddress a = PostalAddress.Parse(Shared);
        PostalAddress b = PostalAddress.Parse("SOMEWHERE 12,\n1234 ANYWHERE");
        Assert.StartsWith("zz|", a.Key, StringComparison.Ordinal);
        Assert.Equal(a.Key, b.Key);
        Assert.NotEqual(a.Key, PostalAddress.Parse("Somewhere 13, 1234 Anywhere").Key);
    }

    [Fact]
    public void JsonRoundTripKeepsTheFlagAndTheRenderings()
    {
        PostalAddress original = PostalAddress.Parse(Shared);
        string json = JsonSerializer.Serialize(original);
        PostalAddress back = JsonSerializer.Deserialize<PostalAddress>(json);
        Assert.Equal(original, back);
        Assert.True(back.Flags.HasFlag(AddressFacts.CountryUndetermined));
        Assert.Equal(original.Key, back.Key);
        Assert.Equal(original.ToString(AddressStyle.Display, AddressLayout.Label), back.ToString(AddressStyle.Display, AddressLayout.Label));
    }
}
