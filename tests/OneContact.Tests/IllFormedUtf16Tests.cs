using System.Buffers;
using RecursiveAI.OneContact.Addresses;
using RecursiveAI.OneContact.Addresses.Expansion;
using RecursiveAI.OneContact.Addresses.Standardization;
using RecursiveAI.OneContact.Addresses.Structural;
using RecursiveAI.OneContact.Names;
using RecursiveAI.OneContact.Phones;
using RecursiveAI.OneContact.Web;

namespace RecursiveAI.OneContact.Tests;

// Ill-formed UTF-16 (lone surrogates, reversed pairs, the noncharacters U+FFFE and U+FFFF) is data like any other: every public entry point
// either handles it as its specification says or refuses it with a reason code. None may throw anything but the documented FormatException of Parse.
public class IllFormedUtf16Tests
{
    private static readonly Region GB = Region.Parse("GB");

    private static readonly string[] Countries = ["US", "CA", "JP", "DE"];

    private static readonly string[] Bad =
    [
        "\uD800", "\uDBFF", "\uDC00", "\uDFFF", "\uDC00\uD800", "\uD800\uD800", "\uDC00\uDC00", "\uFFFE", "\uFFFF", "\uD83D\uDE00\uD83D",
    ];

    private static readonly string[] Samples =
    [
        "+1 (202) 456-1111 ext. 55", "202-456-1111", "1-800-FLOWERS", "+44 20 7946 0958", "tel:+1-202-456-1111;ext=55",
        "\"J. Smith\" <J.Smith+crm@GoogleMail.com>", "\u00fcn\u00ef@b\u00fccher.example", "a@[IPv6:::1]", "x@xn--bcher-kva.de",
        "https://user:pw@www.Example.com:8080/a/b?utm_source=x&q=%C3%A9#frag", "www.b\u00fccher.co.uk/x", "xn--bcher-kva.de", "http://[::1]/", "http://a.b.example.com",
        "Pat Morgan\n10-123 1/2 Rue Principale\nMontr\u00e9al QC  H3Z 2Y7", "350 Fifth Avenue, New York NY 10118", "PO Box 12, Fairview, TX 75069",
        "DR. MAR\u00cdA JOS\u00c9 GARC\u00cdA L\u00d3PEZ", "Mr & Mrs John Smith", "Smith, John Jr.",
        "Acme Holdings Pty Ltd c/o X t/a Y", "The M\u00fcller GmbH",
        "Call +1 202 456 1111 or mail a@example.com",
    ];

    private static readonly string[] FuzzSeeds =
    [
        "https://user:pw@www.Example.com:8080/a/b?utm_source=x&q=%C3%A9#frag", "www.b\u00fccher.co.uk/x", "http://[::1]/", "xn--bcher-kva.de", "http://a.b.example.com",
        "\"J. Smith\" <J.Smith+crm@GoogleMail.com>", "\u00fcn\u00ef@b\u00fccher.example", "a@[IPv6:::1]", "x@xn--bcher-kva.de", "a(c)@b.example",
    ];

    private const string FuzzAlphabet = "\uD800\uDBFF\uDC00\uDFFF\uFFFE\uFFFF\uD83D\uDE00/\\?#@:[]%.&=;<>\" a1-\u00e9\u00df\u200d\u0301";

    public static IEnumerable<string> Inputs()
    {
        yield return string.Empty;
        foreach (string b in Bad)
        {
            yield return b;
            yield return "a" + b;
            yield return b + "a";
            yield return "1" + b + "2";
        }

        foreach (string s in Samples)
        {
            foreach (string b in Bad)
            {
                for (int i = 0; i <= s.Length; i++)
                {
                    yield return s.Insert(i, b);
                }
            }
        }
    }

    // A bounded, seeded smoke run (about a second): surrogate-heavy mutations of URL and email text must only ever parse or be refused,
    // and what parses is stable when its canonical form is parsed again.
    [Fact]
    public void Surrogate_heavy_smoke_fuzz_of_url_and_email_parsers()
    {
        var random = new Random(20261105);
        for (int i = 0; i < 40_000; i++)
        {
            var sb = new System.Text.StringBuilder(FuzzSeeds[random.Next(FuzzSeeds.Length)]);
            for (int edits = random.Next(1, 5); edits > 0; edits--)
            {
                int at = random.Next(sb.Length + 1);
                if (random.Next(3) == 0 && at < sb.Length)
                {
                    sb.Remove(at, 1);
                }
                else
                {
                    sb.Insert(at, FuzzAlphabet[random.Next(FuzzAlphabet.Length)]);
                }
            }

            string x = sb.ToString();
            Guard(x, "WebAddress fuzz", () =>
            {
                if (WebAddress.TryParse(x, out WebAddress w))
                {
                    Assert.Equal(w.Canonical, WebAddress.Parse(w.Canonical).Canonical);
                }
            });
            Guard(x, "EmailAddress fuzz", () =>
            {
                if (EmailAddress.TryParse(x, out EmailAddress e))
                {
                    Assert.Equal(e.Canonical, EmailAddress.Parse(e.Canonical).Canonical);
                }
            });
        }
    }

    // Reports the first failure with the input that caused it, in escaped form.
    private static void Guard(string input, string api, Action call)
    {
        try
        {
            call();
        }
        catch (Exception e)
        {
            string escaped = string.Concat(input.Select(c => c < 0x20 || c > 0x7E ? $"\\u{(int)c:X4}" : c.ToString()));
            throw new Xunit.Sdk.XunitException($"{api} threw {e.GetType().Name} on \"{escaped}\": {e.Message}");
        }
    }

    private static void ParseThrowsOnlyFormat(string input, string api, Action parse) =>
        Guard(input, api, () =>
        {
            try
            {
                parse();
            }
            catch (FormatException)
            {
            }
        });

    [Fact]
    public void Phone_entry_points() => Sweep(Phones);

    [Fact]
    public void Email_and_web_entry_points() => Sweep(EmailWeb);

    [Fact]
    public void Address_entry_points() => Sweep(Addresses);

    [Fact]
    public void Name_company_and_cross_type_entry_points() => Sweep(NamesAndCross);

    private static void Sweep(Action<string> check)
    {
        foreach (string input in Inputs())
        {
            check(input);
        }
    }

    private static void Phones(string x)
    {
        ParseThrowsOnlyFormat(x, "PhoneNumber.Parse", () => PhoneNumber.Parse(x));
        ParseThrowsOnlyFormat(x, "PhoneNumber.Parse(region)", () => PhoneNumber.Parse(x, Region.US));
        Guard(x, "PhoneNumber.TryParse", () =>
        {
            _ = PhoneNumber.TryParse(x, out _);
            _ = PhoneNumber.TryParse(x.AsSpan(), out _);
            _ = PhoneNumber.TryParse(x, Region.US, out _);
            _ = PhoneNumber.TryParse(x.AsSpan(), GB, out _);
            _ = PhoneNumber.TryParse(x.AsSpan(), out _, out _);
            _ = PhoneNumber.TryParse(x.AsSpan(), Region.US, out _, out _);
            if (PhoneNumberParser.Default.TryParse(x, Region.US, keepRawInput: true, out PhoneNumber n, out _) == OperationStatus.Done)
            {
                PhoneNumberParser p = PhoneNumberParser.Default;
                foreach (PhoneFormat f in Enum.GetValues<PhoneFormat>())
                {
                    p.Format(n, f);
                }

                p.FormatOutOfCountry(n, GB);
                p.FormatForMobileDialing(n, Region.US, true);
                p.FormatInOriginalFormat(n, Region.US);
                p.GetNumberType(n);
                p.GetPossibility(n);
                _ = n.Key;
            }

            _ = PhoneNumberParser.Default.ParseMany([x], Region.US, true).ToList();
        });
        Guard(x, "PhoneNumber.Create", () =>
        {
            try
            {
                PhoneNumber.Create(1, x);
                PhoneNumber.Create(1, "2024561111", x);
            }
            catch (ArgumentException)
            {
                // Create documents ArgumentException for a national number or extension that is not digits.
            }
        });
        Guard(x, "PhoneNumberParser.IsVanityNumber", () => PhoneNumberParser.IsVanityNumber(x));
        Guard(x, "AsYouTypeFormatter", () =>
        {
            var f = new AsYouTypeFormatter(Region.US);
            foreach (char c in x)
            {
                f.InputDigit(c);
            }

            f.Clear();
            foreach (char c in x)
            {
                f.InputDigitAndRememberPosition(c);
            }
        });
        Guard(x, "AsYouTypeFormatter (international)", () =>
        {
            var f = new AsYouTypeFormatter(GB);
            f.InputDigit('+');
            foreach (char c in x)
            {
                f.InputDigit(c);
            }
        });
        Guard(x, "PhoneNumberMatcher", () =>
        {
            foreach (PhoneMatchLeniency l in Enum.GetValues<PhoneMatchLeniency>())
            {
                _ = new PhoneNumberMatcher(PhoneNumberParser.Default).FindAll(x, Region.US, l).ToList();
                _ = PhoneNumber.FindAll(x, GB, l).ToList();
            }
        });
        Guard(x, "ShortNumberInfo", () =>
        {
            ShortNumberInfo.Default.IsEmergencyNumber(x, Region.US);
            ShortNumberInfo.Default.ConnectsToEmergencyNumber(x, GB);
        });
    }

    private static void EmailWeb(string x)
    {
        ParseThrowsOnlyFormat(x, "EmailAddress.Parse", () => EmailAddress.Parse(x));
        Guard(x, "EmailAddress.TryParse", () =>
        {
            _ = EmailAddress.TryParse(x, out _);
            _ = EmailAddress.TryParse(x.AsSpan(), out _);
            if (EmailAddress.TryParse(x.AsSpan(), out EmailAddress e, out _) == OperationStatus.Done)
            {
                _ = (e.Canonical, e.LocalPart, e.Domain, e.UnicodeDomain, e.DisplayName, e.TypoSuggestion, e.Key, e.Flags);
            }
        });
        ParseThrowsOnlyFormat(x, "WebAddress.Parse", () => WebAddress.Parse(x));
        Guard(x, "WebAddress.TryParse", () =>
        {
            _ = WebAddress.TryParse(x, out _);
            _ = WebAddress.TryParse(x.AsSpan(), out _);
            if (WebAddress.TryParse(x.AsSpan(), out WebAddress w, out _) == OperationStatus.Done)
            {
                _ = (w.Canonical, w.Scheme, w.Host, w.Port, w.Path, w.Query, w.PublicSuffix, w.RegistrableDomain, w.Key, w.Flags);
            }
        });
        Guard(x, "PublicSuffixList", () =>
        {
            PublicSuffixList.GetPublicSuffix(x);
            PublicSuffixList.GetPublicSuffix(x, false);
            PublicSuffixList.GetRegistrableDomain(x);
            PublicSuffixList.GetRegistrableDomain(x, false);
            PublicSuffixList.GetRegistrableDomain("www." + x + ".com");
            PublicSuffixList.GetPublicSuffix("a." + x);
        });
    }

    private static void Addresses(string x)
    {
        ParseThrowsOnlyFormat(x, "PostalAddress.Parse", () => PostalAddress.Parse(x));
        Guard(x, "PostalAddress.TryParse", () =>
        {
            _ = PostalAddress.TryParse(x, out _);
            _ = PostalAddress.TryParse(x.AsSpan(), out _);
            _ = PostalAddress.TryParse(x.AsSpan(), Region.US, out _, out _);
            _ = PostalAddress.TryParse(x.AsSpan(), Region.CA, out _, out _);
            if (PostalAddress.TryParse(x.AsSpan(), out PostalAddress a, out _) == OperationStatus.Done)
            {
                _ = (a.Key, a.StreetDetail, a.RuleDetail, a.Validate());
                foreach (AddressStyle s in Enum.GetValues<AddressStyle>())
                {
                    a.ToString(s);
                    a.ToString(s, AddressLayout.SingleLine, true);
                }
            }
        });
        Guard(x, "RuleAddressParser", () => RecursiveAI.OneContact.Addresses.Parsing.RuleAddressParser.TryParse(x, Region.Unknown, out _, out _));
        Guard(x, "AddressExpander", () => AddressExpander.Expand(x));
        Guard(x, "AddressExpander (limited)", () => AddressExpander.Expand(x, ExpansionLanguages.English | ExpansionLanguages.French, 4));
        Guard(x, "AddressRegions", () =>
        {
            _ = AddressRegions.TryFindByName(x, out _);
            foreach (string cc in Countries)
            {
                if (AddressRegions.TryGet(Region.Parse(cc), out AddressRegionInfo? info) && info is not null)
                {
                    _ = info.TryMatchSubdivision(x, out _);
                }
            }
        });
        Guard(x, "PostalAddress.Create", () =>
        {
            foreach (string cc in Countries)
            {
                PostalAddress a = PostalAddress.Create(Region.Parse(cc), x, x, x, x, x, x, x, [x, "1 Main St"], x);
                a.Validate();
                a.GetLabelLines();
                a.ToSingleLine();
                _ = a.Key;
                a.ToString(AddressStyle.Postal);
                a.ToString(AddressStyle.Display);
                _ = a.StreetDetail;
            }
        });
    }

    private static void NamesAndCross(string x)
    {
        ParseThrowsOnlyFormat(x, "PersonName.Parse", () => PersonName.Parse(x));
        ParseThrowsOnlyFormat(x, "CompanyName.Parse", () => CompanyName.Parse(x));
        Guard(x, "PersonName.TryParse", () =>
        {
            _ = PersonName.TryParse(x, out _);
            _ = PersonName.TryParse(x.AsSpan(), out _);
            if (PersonName.TryParse(x.AsSpan(), NameHints.ForCountry(Region.US), out PersonName p, out _) == OperationStatus.Done)
            {
                _ = (p.Canonical, p.Key);
            }

            _ = PersonName.TryParse(x.AsSpan(), NameHints.ForLanguage("hu"), out _, out _);
            _ = PersonName.TryParse(x.AsSpan(), new NameHints(Region.US, x), out _, out _);
        });
        Guard(x, "CompanyName.TryParse", () =>
        {
            _ = CompanyName.TryParse(x, out _);
            _ = CompanyName.TryParse(x.AsSpan(), out _);
            if (CompanyName.TryParse(x.AsSpan(), NameHints.ForCountry(GB), out CompanyName c, out _) == OperationStatus.Done)
            {
                _ = (c.Canonical, c.Key);
            }

            _ = CompanyName.TryParse(x.AsSpan(), new NameHints(Region.US, x), out _, out _);
        });
        Guard(x, "Region.TryParse", () =>
        {
            _ = Region.TryParse(x, out _);
            _ = Region.TryParse(x.AsSpan(), out _);
        });
        ParseThrowsOnlyFormat(x, "Region.Parse", () => Region.Parse(x));
        Guard(x, "Contacts", () =>
        {
            _ = Contacts.TryNormalize(x, Region.US, out _);
            _ = Contacts.TryNormalize(x.AsSpan(), Region.Unknown, out _, out _);
            _ = Contacts.SameKey(x, x, Region.US);
            _ = Contacts.SameKey(x, "a@example.com");
        });
        ParseThrowsOnlyFormat(x, "Contacts.Normalize", () => Contacts.Normalize(x, Region.US));
    }
}
