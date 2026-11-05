using System.Buffers;
using System.Text.Json;
using RecursiveAI.OneContact.Keys;

namespace RecursiveAI.OneContact.Tests;

/// <summary>Canonical form, key, flags, tracking-parameter rules and offsets of <see cref="WebAddress"/>.</summary>
public class WebAddressTests
{
    [Fact]
    public void HappyPath()
    {
        WebAddress w = WebAddress.Parse("www.Example.com/?utm_source=x");
        Assert.Equal("https://www.example.com/", w.Canonical);
        Assert.Equal("example.com", w.Key);
        Assert.Equal("example.com", w.RegistrableDomain);
        Assert.Equal("com", w.PublicSuffix);
        Assert.Equal("www.example.com", w.Host);
        Assert.Equal(WebFacts.SchemeAssumed | WebFacts.TrackingParametersRemoved, w.Flags);
    }

    [Theory]
    [InlineData("http://Example.COM:80/a/./b/../c?x=1#frag", "http://example.com/a/c?x=1")]
    [InlineData("https://example.com:443", "https://example.com/")]
    [InlineData("https://example.com:8443/p", "https://example.com:8443/p")]
    [InlineData("  https://BÜCHER.example/straße  ", "https://xn--bcher-kva.example/stra%C3%9Fe")]
    [InlineData("https://user:pw@example.com/", "https://user:pw@example.com/")]
    [InlineData("example.com:8080/x", "https://example.com:8080/x")]
    [InlineData("//example.com/x", "https://example.com/x")]
    [InlineData("mailto:Someone@Example.com", "mailto:Someone@Example.com")]
    [InlineData("http://192.168.0.1.:80/", "http://192.168.0.1/")]
    [InlineData("https://0x7f.1/", "https://127.0.0.1/")]
    [InlineData("https://[2001:DB8:0:0:0:0:0:1]/", "https://[2001:db8::1]/")]
    public void CanonicalForm(string input, string canonical) =>
        Assert.Equal(canonical, WebAddress.Parse(input).Canonical);

    [Theory]
    // Google Analytics 4 campaign parameters (support.google.com/analytics/answer/10917952).
    [InlineData("https://a.com/?utm_source=a&utm_medium=b&utm_campaign=c&utm_term=d&utm_content=e&utm_id=f&utm_source_platform=g&utm_creative_format=h&utm_marketing_tactic=i", "https://a.com/")]
    // Google Ads auto-tagging and the iOS click ids (support.google.com/google-ads/answer/3095550; developers.google.com/google-ads/api/docs/conversions/upload-clicks).
    [InlineData("https://a.com/p?gclid=1&id=7&gbraid=2&wbraid=3", "https://a.com/p?id=7")]
    // Microsoft Advertising auto-tagging of the Microsoft Click ID.
    [InlineData("https://a.com/?msclkid=abc", "https://a.com/")]
    // Meta ClickID (developers.facebook.com/documentation/ads-commerce/conversions-api/parameters/fbp-and-fbc).
    [InlineData("https://a.com/?q=1&fbclid=IwAR2F4", "https://a.com/?q=1")]
    // TikTok Click ID (ads.tiktok.com/resources/help/article/tiktok-click-id).
    [InlineData("https://a.com/?ttclid=xyz&page=2", "https://a.com/?page=2")]
    // X Ads Click ID (docs.x.com/x-ads-api/measurement/web-conversions).
    [InlineData("https://a.com/?twclid=xyz", "https://a.com/")]
    // LinkedIn first-party click id and Mailchimp campaign and member ids.
    [InlineData("https://a.com/?li_fat_id=1&mc_cid=2&mc_eid=3&keep=1", "https://a.com/?keep=1")]
    // Names match ignoring ASCII case; every other parameter stays exactly as written, in order.
    [InlineData("https://a.com/?B=2&UTM_Source=x&a=%20", "https://a.com/?B=2&a=%20")]
    // A query that holds no tracking parameter is left alone, even an empty one.
    [InlineData("https://a.com/?", "https://a.com/?")]
    [InlineData("https://a.com/?utm=1&source=2", "https://a.com/?utm=1&source=2")]
    public void TrackingParametersAreRemoved(string input, string canonical)
    {
        WebAddress w = WebAddress.Parse(input);
        Assert.Equal(canonical, w.Canonical);
        Assert.Equal(canonical != input, (w.Flags & WebFacts.TrackingParametersRemoved) != 0);
    }

    [Fact]
    public void FragmentIsDroppedAndFlagged()
    {
        WebAddress w = WebAddress.Parse("https://a.com/x#top");
        Assert.Equal("https://a.com/x", w.Canonical);
        Assert.True((w.Flags & WebFacts.FragmentRemoved) != 0);
        Assert.Equal(WebFacts.None, WebAddress.Parse("https://a.com/x").Flags);
    }

    [Fact]
    public void SchemeAssumedOnlyWhenMissing()
    {
        Assert.True((WebAddress.Parse("example.org").Flags & WebFacts.SchemeAssumed) != 0);
        Assert.True((WebAddress.Parse("localhost:3000/x").Flags & WebFacts.SchemeAssumed) != 0);
        Assert.Equal(WebFacts.None, WebAddress.Parse("http://example.org").Flags);
        Assert.Equal(WebFacts.NonWebScheme, WebAddress.Parse("ftp://example.org/a").Flags);
        Assert.Equal(WebFacts.NonWebScheme, WebAddress.Parse("mailto:a@b.co").Flags);
    }

    [Fact]
    public void HostFlags()
    {
        Assert.True((WebAddress.Parse("http://10.0.0.1/").Flags & WebFacts.IpHost) != 0);
        Assert.True((WebAddress.Parse("http://[::1]/").Flags & WebFacts.IpHost) != 0);
        Assert.True((WebAddress.Parse("http://münchen.de/").Flags & WebFacts.InternationalizedDomain) != 0);
        Assert.True((WebAddress.Parse("http://u:p@a.com/").Flags & WebFacts.HasCredentials) != 0);
        Assert.Null(WebAddress.Parse("http://10.0.0.1/").RegistrableDomain);
    }

    [Theory]
    [InlineData("https://www.linkedin.com/in/jane-doe-123", WebFacts.SocialLinkedIn)]
    [InlineData("https://uk.linkedin.com/company/acme", WebFacts.SocialLinkedIn)]
    [InlineData("https://www.linkedin.com/feed/", WebFacts.None)]
    [InlineData("https://twitter.com/jack", WebFacts.SocialX)]
    [InlineData("https://x.com/jack", WebFacts.SocialX)]
    [InlineData("https://x.com/jack/status/20", WebFacts.None)]
    [InlineData("https://x.com/home", WebFacts.None)]
    [InlineData("https://x.com/this_handle_is_far_too_long", WebFacts.None)]
    [InlineData("https://github.com/octocat", WebFacts.SocialGitHub)]
    [InlineData("https://github.com/octocat/hello-world", WebFacts.SocialGitHub)]
    [InlineData("https://github.com/orgs/acme/people", WebFacts.SocialGitHub)]
    [InlineData("https://github.com/pricing", WebFacts.None)]
    [InlineData("https://www.facebook.com/acme.inc", WebFacts.SocialFacebook)]
    [InlineData("https://www.facebook.com/sharer/sharer.php?u=x", WebFacts.None)]
    [InlineData("https://www.instagram.com/acme/", WebFacts.SocialInstagram)]
    [InlineData("https://www.instagram.com/p/Cxyz/", WebFacts.None)]
    [InlineData("https://www.youtube.com/@acme", WebFacts.SocialYouTube)]
    [InlineData("https://www.youtube.com/channel/UC123", WebFacts.SocialYouTube)]
    [InlineData("https://www.youtube.com/watch?v=1", WebFacts.None)]
    [InlineData("https://www.tiktok.com/@acme", WebFacts.SocialTikTok)]
    [InlineData("https://mastodon.social/@gargron", WebFacts.SocialMastodonStyle)]
    [InlineData("https://medium.com/@acme", WebFacts.None)]
    [InlineData("https://example.com/about", WebFacts.None)]
    public void SocialProfileShapes(string input, WebFacts expected) =>
        Assert.Equal(expected, WebAddress.Parse(input).Flags & WebFacts.SocialProfile);

    [Theory]
    // Rule 2 and 1: registrable domain, no scheme for http(s), subdomains and "www." dropped.
    [InlineData("https://www.Example.com/", "example.com")]
    [InlineData("http://blog.example.co.uk/", "example.co.uk")]
    [InlineData("https://user.github.io/", "user.github.io")]
    // Rule 3: a non-default port stays.
    [InlineData("https://example.com:8443/", "example.com:8443")]
    // Rule 4: path lowercased, escapes of unreserved characters decoded, empty segments collapsed, default document and trailing slash dropped.
    [InlineData("https://example.com/About/Team/", "example.com/about/team")]
    [InlineData("https://example.com//a///b/index.html", "example.com/a/b")]
    [InlineData("https://example.com/%41b%2Fc%e9", "example.com/ab%2Fc%E9")]
    // Rule 5: query parameters sorted; the fragment and credentials do not count.
    [InlineData("https://u:p@example.com/p?b=2&a=1#x", "example.com/p?a=1&b=2")]
    // Non-web schemes keep their scheme.
    [InlineData("ftp://files.example.com/pub/", "ftp://example.com/pub")]
    [InlineData("mailto:Someone@Example.com", "mailto:someone@example.com")]
    // Hosts with no registrable domain are kept whole; a root dot is dropped.
    [InlineData("http://192.168.0.1/x", "192.168.0.1/x")]
    [InlineData("https://example.com./", "example.com")]
    [InlineData("https://com/", "com")]
    public void KeyV1(string input, string key) =>
        Assert.Equal(key, WebAddress.Parse(input).GetKey(KeyVersion.V1));

    [Fact]
    public void SameKeyForTheSameSite()
    {
        Assert.True(Contacts.SameKey(WebAddress.Parse("www.example.com"), WebAddress.Parse("HTTP://Example.com/?utm_medium=x")));
        Assert.False(Contacts.SameKey(WebAddress.Parse("example.com/a"), WebAddress.Parse("example.com/b")));
    }

    [Fact]
    public void KeyV1GoldenFixture()
    {
        // Published key outputs never change; a rule change would be a new KeyVersion with its own fixture.
        string[] inputs = ["https://www.Example.com/a/B/?b=2&a=1#x", "http://shop.example.co.uk:8080/index.html", "ftp://u@files.example.org/pub/", "http://[::1]/", "mailto:A@B.example"];
        string[] keys = ["example.com/a/b?a=1&b=2", "example.co.uk:8080", "ftp://example.org/pub", "[::1]", "mailto:a@b.example"];
        for (int i = 0; i < inputs.Length; i++)
        {
            Assert.Equal(keys[i], WebKey.Compute(WebAddress.Parse(inputs[i]), KeyVersion.V1));
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://")]
    [InlineData("http://exa mple.com")]
    [InlineData("https://example.com:99999")]
    [InlineData("https://[::1")]
    [InlineData("http://1.2.3.4.5/")]
    public void InvalidInputFails(string input)
    {
        Assert.False(WebAddress.TryParse(input, out WebAddress w));
        Assert.Equal(string.Empty, w.Canonical);
        Assert.Throws<ContactParseException>(() => WebAddress.Parse(input));
    }

    [Fact]
    public void FailureReasons()
    {
        Assert.Equal(ReasonCode.Empty, Detail(" ").PrimaryReason);
        Assert.Equal(ReasonCode.WebHostMissing, Detail("https://").PrimaryReason);
        Assert.Equal(ReasonCode.WebInvalidHost, Detail("https://exa mple.com/").PrimaryReason);
        Assert.Equal(ReasonCode.WebInvalidPort, Detail("https://example.com:70000/").PrimaryReason);
        Assert.Equal("web.invalid-port", ReasonCode.WebInvalidPort.Id);
    }

    private static ParseDetail Detail(string input)
    {
        OperationStatus s = WebAddress.TryParse(input, out _, out ParseDetail d);
        Assert.Equal(OperationStatus.InvalidData, s);
        return d;
    }

    [Fact]
    public void OffsetsPointIntoTheOriginalInput()
    {
        const string Input = "  see https://User:pw@Example.com:8443/a/b?x=1#frag  ";
        // Surrounding white space is ignored, but the text between "see " and the URL is part of the input, so parse only the URL.
        string url = "  https://User:pw@Example.com:8443/a/b?x=1#frag  ";
        OperationStatus s = WebAddress.TryParse(url, out WebAddress w, out ParseDetail d);
        Assert.Equal(OperationStatus.Done, s);
        Assert.Equal(new SourceSpan(2, url.Length - 4), w.Source);
        string Field(string name)
        {
            ParsedField f = d.Fields.Single(x => x.Name == name);
            Assert.Equal(DecisionSource.Rule, f.Source);
            Assert.Equal(1f, f.Confidence);
            return url.Substring(f.Span.Start, f.Span.Length);
        }

        Assert.Equal("https", Field("scheme"));
        Assert.Equal("Example.com", Field("host"));
        Assert.Equal("8443", Field("port"));
        Assert.Equal("/a/b", Field("path"));
        Assert.Equal("x=1", Field("query"));
        Assert.Equal("frag", Field("fragment"));
        Assert.Contains("see", Input, StringComparison.Ordinal);

        // With the scheme assumed the offsets still point into the text as written.
        const string Bare = "www.Example.com/contact?x=1";
        WebAddress.TryParse(Bare, out _, out ParseDetail bare);
        ParsedField host = bare.Fields.Single(x => x.Name == "host");
        Assert.Equal("www.Example.com", Bare.Substring(host.Span.Start, host.Span.Length));
        Assert.DoesNotContain(bare.Fields, x => x.Name == "scheme");
    }

    [Fact]
    public void ContactsNormalizeFindsWebAddressesWithoutTakingEmail()
    {
        Assert.Equal(ContactKind.Web, Contacts.Normalize("www.Example.com/?utm_source=x").Kind);
        Assert.Equal(ContactKind.Web, Contacts.Normalize("https://example.com/@user").Kind);
        Assert.Equal(ContactKind.Web, Contacts.Normalize("example.co.uk/about").Kind);
        Assert.Equal(ContactKind.Web, Contacts.Normalize("Example.COM").Kind);
        Assert.Equal(ContactKind.Web, Contacts.Normalize("https://user@example.com/").Kind);
        Assert.False(Contacts.TryNormalize("jane@example.com", default, out ContactValue v) && v.Kind == ContactKind.Web);
        Assert.False(Contacts.TryNormalize("notes.txt", default, out ContactValue n) && n.Kind == ContactKind.Web);
        Assert.False(Contacts.TryNormalize("1.5", default, out ContactValue d) && d.Kind == ContactKind.Web);
        Assert.False(Contacts.TryNormalize("John.Smith", default, out ContactValue j) && j.Kind == ContactKind.Web);
    }

    [Fact]
    public void JsonRoundTripsCanonicalForm()
    {
        WebAddress w = WebAddress.Parse("Example.com/x?utm_source=a");
        string json = JsonSerializer.Serialize(w, Serialization.ContactJsonContext.Default.WebAddress);
        Assert.Equal("\"https://example.com/x\"", json);
        Assert.Equal(w, JsonSerializer.Deserialize(json, Serialization.ContactJsonContext.Default.WebAddress));
    }

    [Fact]
    public void DefaultValueIsEmpty()
    {
        WebAddress none = default;
        Assert.Equal(string.Empty, none.Canonical);
        Assert.Equal(string.Empty, none.Key);
        Assert.Equal(string.Empty, none.Host);
        Assert.Null(none.RegistrableDomain);
    }
}
