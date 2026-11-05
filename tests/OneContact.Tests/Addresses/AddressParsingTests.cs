using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using RecursiveAI.OneContact.Addresses.Expansion;
using RecursiveAI.OneContact.Addresses.Parsing;
using RecursiveAI.OneContact.Addresses.Standardization;
using RecursiveAI.OneContact.Addresses.Streets;
using RecursiveAI.OneContact.Keys;

namespace RecursiveAI.OneContact.Tests.Addresses;

public class AddressParsingTests
{
    private static ParsedAddress Parse(string text, Region hint = default)
    {
        OperationStatus status = RuleAddressParser.TryParse(text, hint, out ParsedAddress? a, out ParseDetail d);
        Assert.True(status == OperationStatus.Done, $"'{text}' was not read: {d.PrimaryReason}");
        return a!;
    }

    [Theory]
    [InlineData("  123 Main St Apt 4\r\n  Springfield, VA 22162-1234  ")]
    [InlineData("Pat Morgan\n10-123 1/2 Rue Principale\nMontréal QC  H3Z 2Y7")]
    [InlineData("PO Box 12, Fairview, TX 75069")]
    [InlineData("rr 4 box 87a, dover, oh 44622")]
    [InlineData("PSC 3022 Box 684, FPO AE 09397-0007")]
    public void EveryFieldSpanIndexesTheOriginalInput(string text)
    {
        ParsedAddress a = Parse(text);
        Assert.NotEmpty(a.Fields);
        foreach (ParsedAddressField f in a.Fields)
        {
            Assert.True(f.Span.Start >= 0 && f.Span.End <= text.Length, $"{f.Name} is outside the input");
            string slice = text.Substring(f.Span.Start, f.Span.Length);
            if (f.Name != "street.house-number" || !f.Text.Contains(' ', StringComparison.Ordinal))
            {
                Assert.Equal(f.Text.Replace(" ", string.Empty, StringComparison.Ordinal), slice.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("\n", string.Empty, StringComparison.Ordinal).Replace("\r", string.Empty, StringComparison.Ordinal));
            }

            Assert.Equal(DecisionSource.Rule, f.Source);
            Assert.Equal(1f, f.Confidence);
        }

        OperationStatus s = RuleAddressParser.TryParse(text, default, out _, out ParseDetail detail);
        Assert.Equal(OperationStatus.Done, s);
        Assert.Equal(a.Fields.Count, detail.Fields.Count);
    }

    [Fact]
    public void CountryComesFromTheContent()
    {
        ParsedAddress us = Parse("123 Main St, Springfield VA 22162");
        Assert.Equal(Region.US, us.Country);
        Assert.True(us.CountryInferred);
        Assert.True(us.CountryConfidence >= 0.95f);

        ParsedAddress ca = Parse("123 Main St, Calgary AB T2P 1J9");
        Assert.Equal(Region.CA, ca.Country);

        ParsedAddress named = Parse("123 Main St\nSpringfield VA 22162\nUnited States");
        Assert.False(named.CountryInferred);
        Assert.Equal(1f, named.CountryConfidence);
        Assert.NotNull(named.CountryName);
    }

    [Fact]
    public void HintDoesNotOverrideContentButIsReported()
    {
        OperationStatus s = RuleAddressParser.TryParse("123 Main St, Calgary AB T2P 1J9", Region.US, out ParsedAddress? a, out ParseDetail d);
        Assert.Equal(OperationStatus.Done, s);
        Assert.Equal(Region.CA, a!.Country);
        Assert.Contains(d.Issues, i => i.Reason == ReasonCode.AddressCountryConflict && i.Severity == IssueSeverity.Warning);
    }

    [Fact]
    public void UnsupportedHintIsRefused()
    {
        OperationStatus s = RuleAddressParser.TryParse("12 Rue de Rivoli, 75001 Paris", Region.Parse("FR"), out _, out ParseDetail d);
        Assert.Equal(OperationStatus.InvalidData, s);
        Assert.Equal(ReasonCode.AddressUnsupportedCountry, d.PrimaryReason);
    }

    [Fact]
    public void RunOnInputTheRulesCannotSplitNeedsAModel()
    {
        OperationStatus s = RuleAddressParser.TryParse("100 broadway new york ny 10001", default, out ParsedAddress? a, out ParseDetail d);
        Assert.Equal(OperationStatus.InvalidData, s);
        Assert.Null(a);
        Assert.Equal(ReasonCode.AddressModelRequired, d.PrimaryReason);
        Assert.DoesNotContain("package", ReasonCode.AddressModelRequired.Description, StringComparison.OrdinalIgnoreCase);

        // The same address with its commas needs no model.
        Assert.Equal("100 BROADWAY, NEW YORK NY 10001", Parse("100 broadway, new york, ny 10001").ToString());
    }

    [Fact]
    public void NothingIsInvented()
    {
        ParsedAddress zipOnly = Parse("123 Main St, 22042");
        Assert.Null(zipOnly.Locality);
        Assert.Null(zipOnly.AdminArea);
        Assert.Equal("123 MAIN ST, 22042", zipOnly.ToString());

        ParsedAddress fsa = Parse("123 Main St, M5V 2T6");
        Assert.Null(fsa.AdminArea);
        Assert.Null(fsa.Locality);
        Assert.Equal(Region.CA, fsa.Country);
    }

    [Theory]
    [InlineData("123 Main St, Springfield, VA 22162")]
    [InlineData("10-123 1/2 rue main se\nmontréal qc h3z2y7")]
    [InlineData("PO BOX 4001 STN A, Victoria BC V8X 3X4")]
    [InlineData("1234 calle aurora, mayaguez, pr 00680")]
    public void CanonicalFormParsesBackToItself(string text)
    {
        PostalAddress p = PostalAddress.Parse(text);
        PostalAddress q = PostalAddress.Parse(p.Canonical);
        Assert.Equal(p.Canonical, q.Canonical);
        Assert.Equal(p.Key, q.Key);
        Assert.Equal(p, q);
    }

    [Fact]
    public void GoldAddressesAreStableUnderReparse()
    {
        int checkedCount = 0;
        foreach (string[] row in AddressTestData.Rows("testdata/addresses/parse-gold.tsv", 4))
        {
            string text = row[2].Replace("\\n", "\n", StringComparison.Ordinal);
            if (!PostalAddress.TryParse(text, out PostalAddress p))
            {
                continue;
            }

            checkedCount++;
            PostalAddress q = PostalAddress.Parse(p.Canonical);
            Assert.True(p.Canonical == q.Canonical, $"{row[0]}: '{p.Canonical}' reparsed as '{q.Canonical}'");
            Assert.True(p.Key == q.Key, $"{row[0]}: key changed on reparse");
        }

        Assert.True(checkedCount > 290);
    }

    [Fact]
    public void KeyTreatsSpellingsOfOneAddressAsOne()
    {
        string[] same =
        [
            "123 North Main Street Apartment 4, Springfield, Virginia 22162-1234",
            "123 N Main St Apt 4, springfield va 22162",
            "123 N. MAIN ST #4\nSPRINGFIELD, VA 22162-9999",
            "123 n main st unit 4, Springfield VA 22162",
        ];
        string key = PostalAddress.Parse(same[0]).Key;
        foreach (string s in same)
        {
            Assert.Equal(key, PostalAddress.Parse(s).Key);
        }

        Assert.NotEqual(key, PostalAddress.Parse("124 N Main St Apt 4, Springfield VA 22162").Key);
        Assert.NotEqual(key, PostalAddress.Parse("123 N Main St Apt 4, Springfield VA 22163").Key);

        // Canada Post section 4.4: a unit before the civic number and a unit after the street are one address.
        Assert.Equal(PostalAddress.Parse("10-123 Main St, Montreal QC H3Z 2Y7").Key, PostalAddress.Parse("123 Main Street Apt 10, Montréal QC H3Z2Y7").Key);

        // Recipient lines do not enter the key; equality of the canonical form still tells the two values apart.
        PostalAddress a = PostalAddress.Parse("Pat Morgan\n123 Main St\nSpringfield VA 22162");
        PostalAddress b = PostalAddress.Parse("Alex Rivera\n123 Main St\nSpringfield VA 22162");
        Assert.Equal(a.Key, b.Key);
        Assert.NotEqual(a, b);
        Assert.True(Contacts.SameKey(a, b));
    }

    [Fact]
    public void KeyVersionOneGolden()
    {
        // Published key version 1 outputs never change; a rule change needs a new KeyVersion and its own fixtures.
        foreach (string[] row in AddressTestData.Rows("testdata/addresses/key-v1-golden.tsv", 2))
        {
            Assert.Equal(row[1], PostalAddress.Parse(row[0].Replace("\\n", "\n", StringComparison.Ordinal)).GetKey(KeyVersion.V1));
        }
    }

    [Fact]
    public void FlagsReportFacts()
    {
        Assert.True(Parse("PO Box 12, Fairview TX 75069").Facts.HasFlag(AddressFacts.PostOfficeBox));
        Assert.True(Parse("RR 2 Box 9, Dover OH 44622").Facts.HasFlag(AddressFacts.Route));
        Assert.True(Parse("PSC 1 Box 2, APO AE 09001").Facts.HasFlag(AddressFacts.Military));
        Assert.True(Parse("1234 Calle Aurora, Mayaguez PR 00680").Facts.HasFlag(AddressFacts.Territory));
        Assert.True(Parse("123 Main St Apt 4, Dover OH 44622").Facts.HasFlag(AddressFacts.HasUnit));
    }

    [Fact]
    public void DisplayIsTitleCaseWithAuthorityAbbreviations()
    {
        ParsedAddress a = Parse("123 N MAIN ST APT 4, SPRINGFIELD VA 22162");
        Assert.Equal("123 N Main St Apt 4\nSpringfield, VA 22162", a.ToString(AddressStyle.Display));
        Assert.Equal("123 N Main St Apt 4, Springfield, VA 22162", a.ToString(AddressStyle.Display, AddressLayout.SingleLine));
        Assert.Equal("123 N MAIN ST APT 4\nSPRINGFIELD VA  22162", a.ToString(AddressStyle.Postal));
        Assert.Equal("123 N MAIN ST APT 4, SPRINGFIELD VA 22162", a.ToString(AddressStyle.Postal, AddressLayout.SingleLine));
        Assert.Equal("PO Box 12\nFairview, TX 75069", Parse("PO BOX 12\nFAIRVIEW TX 75069").ToString(AddressStyle.Display));
        Assert.Equal("5th Ave\nNew York, NY 10001".Replace("5th Ave", "350 5th Ave"), Parse("350 5TH AVE\nNEW YORK NY 10001").ToString(AddressStyle.Display));
        Assert.Equal("123 Rue Main\nMontréal, QC H3Z 2Y7", Parse("123 RUE MAIN\nMONTRÉAL QC H3Z 2Y7").ToString(AddressStyle.Display));
    }

    [Fact]
    public void ResultsIgnoreTheCurrentCulture()
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            string text = "12 ISTANBUL ST, IRVINE CA 92618";
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            string invariant = Parse(text).ToString(AddressStyle.Display);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            Assert.Equal(invariant, Parse(text).ToString(AddressStyle.Display));
            Assert.Equal(PostalAddress.Parse(text).Key, PostalAddress.Parse(text.ToLowerInvariant()).Key);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void StreetPartsAreExposedWithRules()
    {
        ParsedAddress a = Parse("1401 S. Main St. Apt 4B\nPlummer's Landing, KY 41081-1411");
        StreetLine line = Assert.Single(a.DeliveryLines);
        Assert.Equal("1401", line.Find(StreetPartRole.HouseNumber)!.Value.Standard);
        Assert.Equal("S", line.Find(StreetPartRole.PreDirectional)!.Value.Standard);
        Assert.Equal("MAIN", line.Find(StreetPartRole.StreetName)!.Value.Standard);
        Assert.Equal("ST", line.Find(StreetPartRole.StreetType)!.Value.Standard);
        Assert.Equal("APT", line.Find(StreetPartRole.UnitDesignator)!.Value.Standard);
        Assert.Equal("4B", line.Find(StreetPartRole.UnitNumber)!.Value.Standard);
        Assert.StartsWith("pub28-", line.Find(StreetPartRole.PreDirectional)!.Value.Rule, StringComparison.Ordinal);
        Assert.Equal("PLUMMER'S LANDING", a.Locality!.Value.Standard);
        Assert.Equal("KY", a.AdminArea!.Value.Standard);
        Assert.Equal("41081-1411", a.PostalCode!.Value.Standard);
    }

    [Fact]
    public void UnitBelowTheStreetLineJoinsIt()
    {
        Assert.Equal("123 MAIN ST APT 4, SPRINGFIELD VA 22162", Parse("123 Main St, Apt 4, Springfield, VA 22162").ToString());
        Assert.Equal("APT 4, 123 MAIN ST, SPRINGFIELD VA 22162", Parse("Apt 4\n123 Main St\nSpringfield VA 22162").ToString());
    }

    [Fact]
    public void ExpansionGivesTheFullFormFirstAndTheWrittenFormLast()
    {
        IReadOnlyList<string> v = AddressExpander.Expand("30 W 26th St, Apt 4");
        Assert.Equal("30 west 26 street apartment 4", v[0]);
        Assert.Equal("30 w 26th st apt 4", v[^1]);
        Assert.Contains("30 west 26th street apartment 4", v);

        IReadOnlyList<string> fr = AddressExpander.Expand("10 boul. Saint-Laurent");
        Assert.Contains(fr, s => s.Contains("boulevard", StringComparison.Ordinal));
        Assert.Empty(AddressExpander.Expand("   "));
        Assert.Equal(AddressExpander.Expand("30 W 26th St"), AddressExpander.Expand("30 W 26th St"));
        Assert.True(AddressExpander.Expand("1 N A St", maxVariants: 3).Count <= 3);
    }

    [Fact]
    public void ParserNeverThrowsAndSpansStayInsideTheInput()
    {
        int seconds = int.TryParse(Environment.GetEnvironmentVariable("ONECONTACT_FUZZ_SECONDS"), NumberStyles.None, CultureInfo.InvariantCulture, out int s) ? s : 2;
        string[] seeds =
        [
            "123 Main St Apt 4, Springfield, VA 22162-1234", "PO Box 12\nFairview TX 75069", "RR 4 Box 87A, Dover OH 44622", "10-123 1/2 Rue Principale, Montréal QC H3Z 2Y7",
            "PSC 3022 Box 684\nAPO AE 09397-0007", "URB Los Olmos\n1234 Calle Aurora\nPonce PR 00731", "GD STN A\nCalgary AB T0H 1A0", "\u00e9\u0301 123 \ud83d\ude00 St, X",
        ];
        Random rng = new(20261105);
        string alphabet = " ,\n\r\t#-/.0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz\u00e9\u00c9\u0301\ud83d\ude00\ud83d";
        long runs = 0;
        Stopwatch clock = Stopwatch.StartNew();
        while (clock.Elapsed.TotalSeconds < seconds)
        {
            char[] chars = seeds[rng.Next(seeds.Length)].ToCharArray();
            int edits = rng.Next(0, 6);
            List<char> list = [.. chars];
            for (int e = 0; e < edits; e++)
            {
                int pos = list.Count == 0 ? 0 : rng.Next(list.Count);
                switch (rng.Next(3))
                {
                    case 0 when list.Count > 0:
                        list.RemoveAt(pos);
                        break;
                    case 1:
                        list.Insert(pos, alphabet[rng.Next(alphabet.Length)]);
                        break;
                    default:
                        if (list.Count > 0)
                        {
                            list[pos] = alphabet[rng.Next(alphabet.Length)];
                        }

                        break;
                }
            }

            if (rng.Next(50) == 0)
            {
                list.AddRange(new string('9', rng.Next(1, 3000)));
            }

            string text = new([.. list]);
            runs++;
            OperationStatus status = RuleAddressParser.TryParse(text, default, out ParsedAddress? a, out ParseDetail d);
            if (status == OperationStatus.Done)
            {
                Assert.NotNull(a);
                foreach (ParsedAddressField f in a.Fields)
                {
                    Assert.True(f.Span.Start >= 0 && f.Span.End <= text.Length, $"span outside input for '{text}'");
                }

                _ = a.ToString(AddressStyle.Display, AddressLayout.SingleLine, includeCountry: true);
                _ = AddressKey.Compute(a, KeyVersion.V1);
            }
            else
            {
                Assert.False(d.PrimaryReason.IsNone && d.Issues.Count == 0);
            }

            _ = AddressExpander.Expand(text);
        }

        Assert.True(runs > 100);
        if (seconds > 2)
        {
            Console.WriteLine($"address parser smoke fuzz: {runs} executions in {seconds} s, no failures");
        }
    }
}
