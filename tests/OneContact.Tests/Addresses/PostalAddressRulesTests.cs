using System.Text.Json;
using RecursiveAI.OneContact.Addresses;
using RecursiveAI.OneContact.Addresses.Standardization;
using RecursiveAI.OneContact.Addresses.Streets;
using RecursiveAI.OneContact.Serialization;

namespace RecursiveAI.OneContact.Tests.Addresses;

public class PostalAddressRulesTests
{
    [Fact]
    public void ParsedAddressFillsTheFieldModel()
    {
        PostalAddress a = PostalAddress.Parse("Pat Morgan\n1 main street apt 4\nspringfield, virginia 22162");
        Assert.Equal(Region.US, a.Country);
        Assert.Equal("VA", a.AdminArea);
        Assert.Equal("springfield", a.Locality);
        Assert.Equal("22162", a.PostalCode);
        Assert.Equal("Pat Morgan", a.Recipient);
        Assert.Equal("1 MAIN ST APT 4", Assert.Single(a.StreetLines));
        Assert.Equal(AddressCoverage.Full, a.Coverage);
        Assert.NotNull(a.StreetDetail[0].Find(StreetPartRole.HouseNumber));
        Assert.Empty(a.Validate());
    }

    [Fact]
    public void StructuralChecksComeFromTheFieldModel()
    {
        PostalAddress a = PostalAddress.Parse("1 Main St, Springfield, VA 90210");
        Assert.NotEmpty(a.Validate());
        PostalAddress ca = PostalAddress.Parse("1 Main St, Calgary, AB T2P 1J9");
        Assert.Empty(ca.Validate());
    }

    [Fact]
    public void TryParseWithCountryUsesTheCountryAsHint()
    {
        Assert.Equal(System.Buffers.OperationStatus.Done, PostalAddress.TryParse("1 Main St, Springfield 22162", Region.US, out PostalAddress a, out _));
        Assert.Equal(Region.US, a.Country);
        Assert.Equal(System.Buffers.OperationStatus.InvalidData, PostalAddress.TryParse("1 Main St, Springfield 22162", Region.Parse("JP"), out _, out ParseDetail d));
        Assert.Equal(ReasonCode.AddressFreeTextRefused, d.PrimaryReason);
    }

    [Fact]
    public void StylesRenderThroughTheValue()
    {
        PostalAddress a = PostalAddress.Parse("1 main street apt 4, springfield, va 22162");
        Assert.Equal("1 MAIN ST APT 4\nSPRINGFIELD VA  22162", a.ToString(AddressStyle.Postal));
        Assert.Equal("1 Main St Apt 4, Springfield, VA 22162", a.ToString(AddressStyle.Display, AddressLayout.SingleLine));
    }

    [Fact]
    public void AddressMadeFromFieldsGetsTheSameRenderings()
    {
        PostalAddress made = PostalAddress.Create(Region.US, adminArea: "VA", locality: "Springfield", postalCode: "22162", streetLines: ["1 Main Street Apt 4"]);
        PostalAddress parsed = PostalAddress.Parse("1 main street apt 4, springfield, va 22162");
        Assert.Equal(parsed.ToString(AddressStyle.Postal), made.ToString(AddressStyle.Postal));
        Assert.Equal(parsed.Key, made.Key);
        PostalAddress other = PostalAddress.Create(Region.Parse("JP"), locality: "Tokyo");
        Assert.Empty(other.StreetDetail);
        Assert.NotEmpty(other.ToString(AddressStyle.Postal));
    }

    [Fact]
    public void JsonRoundTripsParsedAndCreatedAddresses()
    {
        PostalAddress[] values =
        [
            PostalAddress.Parse("Pat Morgan\n10-123 1/2 Rue Principale\nMontréal QC  H3Z 2Y7"),
            PostalAddress.Parse("PO Box 12, Fairview, TX 75069"),
            PostalAddress.Create(Region.US, adminArea: "VA", locality: "Springfield", postalCode: "22162", streetLines: ["1 Main St"]),
            PostalAddress.Create(Region.Parse("JP"), locality: "Tokyo", language: "ja-Latn"),
        ];
        foreach (PostalAddress v in values)
        {
            string json = JsonSerializer.Serialize(v, ContactJsonContext.Default.PostalAddress);
            PostalAddress back = JsonSerializer.Deserialize(json, ContactJsonContext.Default.PostalAddress);
            Assert.Equal(v, back);
            Assert.Equal(v.Country, back.Country);
            Assert.Equal(v.Flags, back.Flags);
            Assert.Equal(v.Locality, back.Locality, ignoreCase: true);
            Assert.Equal(v.StreetLines.Select(l => l.ToUpperInvariant()), back.StreetLines.Select(l => l.ToUpperInvariant()));
            Assert.Equal(v.Key, back.Key);
        }

        Assert.Equal(PostalAddress.Parse("1 Main St, Springfield VA 22162"), JsonSerializer.Deserialize("\"1 Main St, Springfield VA 22162\"", ContactJsonContext.Default.PostalAddress));
    }

    [Fact]
    public void NormalizeRoutesAnAddress()
    {
        ContactValue v = Contacts.Normalize("1 Main St, Springfield VA 22162");
        Assert.Equal(ContactKind.Address, v.Kind);
        Assert.Equal(ContactKind.Email, Contacts.Normalize("a@example.com").Kind);
        Assert.Equal(ContactKind.Web, Contacts.Normalize("www.example.com/x").Kind);
    }
}
