using System.Buffers;
using System.Text;
using System.Text.Json;
using RecursiveAI.OneContact.Email;
using RecursiveAI.OneContact.Phones;
using RecursiveAI.OneContact.Phones.Metadata;
using RecursiveAI.OneContact.Text.Idna;
using RecursiveAI.OneContact.Web;

namespace RecursiveAI.OneContact.Tests;

/// <summary>
/// The parsers read the common plain shapes (web addresses, dot-atom mailboxes, plain phone numbers, whole-input pattern tests) by short
/// routes. These tests hold each short route to the general one over the conformance data and generated inputs: the results must be equal,
/// field by field.
/// </summary>
public sealed class ShortcutEquivalenceTests
{
    private static string HereFile([System.Runtime.CompilerServices.CallerFilePath] string here = "") => here;

    // ---- URL -------------------------------------------------------------------------------------------------------------------

    private static string Describe(bool ok, UrlRecord url, UrlFailure failure, in UrlSpans s) =>
        ok
            ? $"{url.Scheme}|{url.Username}|{url.Password}|{url.Host}|{url.HostKind}|{url.Port}|{string.Join('/', url.Path)}|{url.OpaquePath}|{url.Query}|{url.Fragment}|{url.Serialize(excludeFragment: false)}|{s.SchemeEnd},{s.HostStart},{s.HostEnd},{s.PortStart},{s.PortEnd},{s.PathStart},{s.QueryStart},{s.FragmentStart},{s.End}"
            : "fail:" + failure;

    private static int CompareUrl(string input, List<string> differences)
    {
        UrlSpans a = UrlSpans.Empty;
        bool okA = UrlParser.TryParse(input, null, out UrlRecord ra, out UrlFailure fa, ref a);
        UrlSpans b = UrlSpans.Empty;
        bool okB = UrlParser.TryParseByStateMachine(input, out UrlRecord rb, out UrlFailure fb, ref b);
        string da = Describe(okA, ra, fa, a);
        string db = Describe(okB, rb, fb, b);
        if (da != db)
        {
            differences.Add($"'{input}': shortcut {da} machine {db}");
        }

        UrlSpans c = UrlSpans.Empty;
        return UrlParser.TryParsePlain(input, out _, ref c) ? 1 : 0;
    }

    [Fact]
    public void Url_shortcut_equals_the_state_machine_over_the_conformance_data_and_generated_addresses()
    {
        List<string> differences = [];
        int shortcut = 0;
        int total = 0;
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "testdata", "wpt", "urltestdata.json")));
        foreach (JsonElement e in doc.RootElement.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string? input = WptUrlTests.Str(e.GetProperty("input"));
            if (input is null)
            {
                continue;
            }

            total++;
            shortcut += CompareUrl(input, differences);
        }

        string[] schemes = ["http://", "https://", "HTTP://", "Https://", "http:", "ftp://", "http://user@", "http://u:p@"];
        string[] hosts = ["example.com", "a.b.c.example.co.uk", "EXAMPLE.com", "localhost", "1.2.3.4", "xn--bcher-kva.example", "exa_mple.com", "a..b", "-a.com", "a-.com", "a.com.", "[::1]", "münchen.de", "a b.com", "0x7f.1", "9.9.9.9.9", "a%41.com", "e.com:80", "e.com:443", "e.com:8080", "e.com:99999", "e.com:", "e.com:0"];
        string[] paths = ["", "/", "/a", "/a/b/", "/a/./b", "/a/../b", "/a/%2e%2E/b", "/a b", "/a%20b", "/ä", "/a\\b", "//", "/a?b", "/~x/_y-z", "/a;b=c", "/a|b", "/a\"b", "/a{b}"];
        string[] queries = ["", "?", "?a=b", "?a=b&c=d", "?a=b c", "?a='b'", "?q=ä", "?a=%zz", "?a=\"x\"", "?a=<>"];
        string[] fragments = ["", "#", "#top", "#a b", "#a`b", "#ä", "#a#b"];
        foreach (string sc in schemes)
        {
            foreach (string h in hosts)
            {
                foreach (string p in paths)
                {
                    string q = queries[(sc.Length + h.Length + p.Length) % queries.Length];
                    string f = fragments[(sc.Length * 3 + h.Length + p.Length) % fragments.Length];
                    total++;
                    shortcut += CompareUrl(sc + h + p + q + f, differences);
                    total++;
                    shortcut += CompareUrl("  " + sc + h + p + "\t" + q + "\n" + f + " ", differences);
                }
            }
        }

        foreach (string q in queries)
        {
            foreach (string f in fragments)
            {
                total++;
                shortcut += CompareUrl("https://example.com" + q + f, differences);
                total++;
                shortcut += CompareUrl("https://example.com/p" + q + f, differences);
            }
        }

        Assert.True(shortcut > 100, $"the shortcut applied to only {shortcut} of {total} inputs");
        Assert.True(differences.Count == 0, $"{differences.Count} of {total} differ (shortcut applied to {shortcut}):\n" + string.Join('\n', differences.Take(25)));
    }

    [Fact]
    public void Idna_ascii_answers_equal_the_table_search()
    {
        for (int cp = 0; cp < 128; cp++)
        {
            IdnaStatus fast = IdnaTables.GetStatus(cp, out ReadOnlySpan<char> fastMapping);
            IdnaStatus slow = IdnaTables.SearchStatus(cp, out ReadOnlySpan<char> slowMapping);
            Assert.True(fast == slow && fastMapping.SequenceEqual(slowMapping), $"U+{cp:X4}: {fast} '{fastMapping}' vs {slow} '{slowMapping}'");
        }
    }

    // ---- Public suffix ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void Suffix_analysis_equals_the_general_lookups()
    {
        List<string> hosts = [];
        foreach (string line in File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "testdata", "psl", "test_psl.txt")))
        {
            string t = line.Trim();
            int open = t.IndexOf('\'', StringComparison.Ordinal);
            if (t.StartsWith("checkPublicSuffix(", StringComparison.Ordinal) && open > 0)
            {
                int close = t.IndexOf('\'', open + 1);
                hosts.Add(t[(open + 1)..close]);
            }
        }

        string[] labels = ["a", "www", "com", "uk", "co", "jp", "kobe", "city", "github", "io", "ck", "ac", "tokyo", "bar", "xn--abc", "Example", "blogspot", "s3", "us-east-1", "amazonaws", "compute", "foo", "bd", "kawasaki", "mm"];
        var rng = new Random(20261106);
        for (int i = 0; i < 20000; i++)
        {
            int n = rng.Next(1, 8);
            hosts.Add(string.Join('.', Enumerable.Range(0, n).Select(_ => labels[rng.Next(labels.Length)])));
        }

        hosts.AddRange(["", ".", "a.", ".a", "a..b", "COM", "münchen.de", "a.b.c.d.e.f.g.h.i.j.k.l.m.n.o.p"]);
        int differing = 0;
        List<string> samples = [];
        foreach (string host in hosts)
        {
            PublicSuffixList.Analyze(host, out string? suffix, out string? registrable);
            string? wantSuffix = PublicSuffixList.GetPublicSuffix(host);
            string? wantRegistrable = PublicSuffixList.GetRegistrableDomain(host);
            if (suffix != wantSuffix || registrable != wantRegistrable)
            {
                differing++;
                samples.Add($"'{host}': {suffix}/{registrable} vs {wantSuffix}/{wantRegistrable}");
            }
        }

        Assert.True(differing == 0, $"{differing} of {hosts.Count} differ:\n" + string.Join('\n', samples.Take(25)));
    }

    // ---- Email -----------------------------------------------------------------------------------------------------------------

    private static string Describe(OperationStatus status, EmailAddress v, ParseDetail d) =>
        status == OperationStatus.Done
            ? $"{v.Canonical}|{v.Flags}|{v.DisplayName}|{v.Source}|{v.Key}|" + string.Join(';', d.Fields.Select(f => $"{f.Name}@{f.Span}")) + "|" + string.Join(';', d.Issues.Select(i => i.ToString()))
            : "fail:" + d.PrimaryReason.Id + "@" + string.Join(';', d.Issues.Select(i => i.ToString()));

    private static void CompareEmail(string input, List<string> differences)
    {
        OperationStatus sa = EmailAddressParser.TryParse(input, allowPlainShortcut: true, out EmailAddress va, out ParseDetail da);
        OperationStatus sb = EmailAddressParser.TryParse(input, allowPlainShortcut: false, out EmailAddress vb, out ParseDetail db);
        string a = Describe(sa, va, da);
        string b = Describe(sb, vb, db);
        if (a != b)
        {
            differences.Add($"'{input}': shortcut {a} grammar {b}");
        }

        bool quick = EmailAddressParser.TryParseValue(input, out EmailAddress vq);
        if (quick != (sb == OperationStatus.Done) || (quick && vq.Canonical != vb.Canonical))
        {
            differences.Add($"'{input}': value form disagrees");
        }
    }

    [Fact]
    public void Email_shortcut_equals_the_grammar_over_the_corpus_and_generated_addresses()
    {
        List<string> differences = [];
        List<string> inputs = [];
        string dir = EmailConformanceTests.CorpusDirectory(Path.Combine(Path.GetDirectoryName(HereFile())!, "Email", "x.cs"));
        foreach (string file in Directory.GetFiles(dir, "*.cases"))
        {
            foreach (string line in File.ReadAllLines(file, Encoding.UTF8))
            {
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                string[] c = line.Split(" \u00a6 ");
                if (c.Length > 2)
                {
                    inputs.Add(EmailConformanceTests.Unescape(c[2]));
                }
            }
        }

        string iso = UpstreamFiles.Get("isemail", "test/tests.xml");
        foreach (System.Xml.Linq.XElement t in System.Xml.Linq.XDocument.Load(iso).Descendants("test"))
        {
            inputs.Add(t.Element("address")?.Value ?? string.Empty);
        }

        string[] locals = ["a", "john.smith", "J.Smith+crm", "a..b", ".a", "a.", "a b", "\"a b\"", "a(c)", "Ünï", "a!#$%&'*+-/=?^_`{|}~", "x@y", "", "a\u00a0", "1", new string('a', 70)];
        string[] domains = ["example.com", "GoogleMail.com", "a-b.example", "-a.com", "a-.com", "a..com", "a.com.", ".com", "com", "1.2.3.4", "[1.2.3.4]", "münchen.de", "xn--bcher-kva.de", "a_b.com", "e.c", "example.123", new string('b', 70) + ".com", "mailinator.com", "gmial.com", "x.co.uk"];
        foreach (string l in locals)
        {
            foreach (string d in domains)
            {
                inputs.Add(l + "@" + d);
                inputs.Add(" " + l + "@" + d + "\t");
                inputs.Add("<" + l + "@" + d + ">");
                inputs.Add("Jane Smith <" + l + "@" + d + ">");
                inputs.Add("\"J. Smith\" <" + l + "@" + d + "> (work)");
                inputs.Add("=?UTF-8?Q?J=C3=BCrgen?= <" + l + "@" + d + ">");
                inputs.Add("<" + l + "@" + d + "> extra");
            }
        }

        foreach (string i in inputs)
        {
            CompareEmail(i, differences);
        }

        Assert.True(differences.Count == 0, $"{differences.Count} of {inputs.Count} differ:\n" + string.Join('\n', differences.Take(25)));
    }

    // ---- Phone patterns --------------------------------------------------------------------------------------------------------

    [Fact]
    public void Pattern_automaton_equals_the_backtracking_matcher_on_every_metadata_pattern()
    {
        PhoneMetadata metadata = PhoneMetadata.Default;
        var rng = new Random(20261106);
        int patterns = 0;
        int inputs = 0;
        List<string> differences = [];
        foreach (Region region in metadata.Regions)
        {
            if (!metadata.TryGetRegion(region, out RegionMetadata r))
            {
                continue;
            }

            for (DescKind kind = DescKind.General; kind <= DescKind.Uan; kind++)
            {
                Desc d = r.GetDesc(kind);
                if (d.Pattern is null)
                {
                    continue;
                }

                PhoneRegex re = PhoneRegex.Compile(d.Pattern.Source);
                patterns++;
                string[] seeds = d.Example is { } ex ? [ex] : [];
                var candidates = new List<string>();
                foreach (string s in seeds)
                {
                    candidates.Add(s);
                    candidates.Add(s[..(s.Length / 2)]);
                    candidates.Add(s + "1");
                    candidates.Add(s + "12345");
                    for (int k = 0; k < 6; k++)
                    {
                        char[] m = s.ToCharArray();
                        m[rng.Next(m.Length)] = (char)('0' + rng.Next(10));
                        candidates.Add(new string(m));
                    }
                }

                for (int k = 0; k < 8; k++)
                {
                    candidates.Add(new string(Enumerable.Range(0, rng.Next(2, 14)).Select(_ => (char)('0' + rng.Next(10))).ToArray()));
                }

                candidates.Add("");
                candidates.Add("12a45");
                candidates.Add("١٢٣٤٥٦٧");

                int[] groups = new int[2 * (re.GroupCount + 1)];
                // The automaton is built after a few whole-input calls; warm it up before comparing.
                for (int w = 0; w < 6; w++)
                {
                    re.IsMatch("0");
                }

                foreach (string c in candidates)
                {
                    inputs++;
                    bool fast = re.IsMatch(c);
                    bool slow = re.Match(c, groups);
                    if (fast != slow)
                    {
                        differences.Add($"{region} {kind} /{d.Pattern.Source}/ '{c}': automaton {fast} matcher {slow}");
                    }
                }
            }
        }

        Assert.True(patterns > 1000);
        Assert.True(differences.Count == 0, $"{differences.Count} of {inputs} differ over {patterns} patterns:\n" + string.Join('\n', differences.Take(25)));
    }

    [Fact]
    public void Vectorized_skipping_finds_the_same_matches_as_the_character_loop()
    {
        string[] patterns = ["(?:[(+]\\s{0,4}){0,2}\\p{Nd}{1,20}(?:[- .]{0,4}\\p{Nd}{1,20}){0,20}", "\\d{3}-\\d{4}", "[a-c]+@", "x(?:yz)?"];
        char[] alphabet = "0123456789 -.()+@abcxyz\u0661\u0662\u00e9\ud835\udfce\ud835\udfcf\n".ToCharArray();
        var rng = new Random(20261106);
        int searches = 0;
        foreach (string pattern in patterns)
        {
            PhoneRegex plain = PhoneRegex.Compile(pattern, PhoneRegex.CaseMode.Unicode);
            PhoneRegex skipping = PhoneRegex.Compile(pattern, PhoneRegex.CaseMode.Unicode).WithVectorSkip();
            for (int t = 0; t < 400; t++)
            {
                char[] chars = new char[rng.Next(0, 200)];
                for (int i = 0; i < chars.Length; i++)
                {
                    chars[i] = alphabet[rng.Next(alphabet.Length)];
                }

                string text = new(chars);
                int from = 0;
                int fromSkip = 0;
                while (true)
                {
                    searches++;
                    bool a = plain.Find(text, from, default, out int s1, out int e1);
                    bool b = skipping.Find(text, fromSkip, default, out int s2, out int e2);
                    Assert.True(a == b && s1 == s2 && e1 == e2, $"/{pattern}/ in '{text}' from {from}: plain {a} {s1}-{e1}, skipping {b} {s2}-{e2}");
                    if (!a)
                    {
                        break;
                    }

                    from = fromSkip = Math.Max(e1, s1 + 1);
                }
            }
        }

        Assert.True(searches > 1000);
    }

    // ---- Phone numbers ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void Phone_plain_route_equals_the_detailed_route_and_the_cached_type_equals_the_computed_one()
    {
        var rng = new Random(20261106);
        int cases = 0;
        List<string> differences = [];
        foreach (Region region in PhoneMetadata.Default.Regions)
        {
            foreach (PhoneNumberType type in Enum.GetValues<PhoneNumberType>())
            {
                PhoneNumber? example = PhoneNumber.GetExample(region, type);
                if (example is not { } ex)
                {
                    continue;
                }

                List<string> texts =
                [
                    ex.E164,
                    ex.Format(PhoneFormat.National),
                    ex.Format(PhoneFormat.International),
                    ex.Format(PhoneFormat.Rfc3966),
                    ex.Format(PhoneFormat.E164).Replace("+", "00", StringComparison.Ordinal),
                    ex.NationalNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ex.E164 + " ext. 12",
                    "tel:" + ex.E164,
                    ex.Format(PhoneFormat.National).Replace(" ", "-", StringComparison.Ordinal),
                    ex.Format(PhoneFormat.National) + "x",
                    "1-800-FLOWERS",
                ];
                string e = ex.E164;
                for (int k = 0; k < 6; k++)
                {
                    char[] m = e.ToCharArray();
                    m[rng.Next(1, m.Length)] = (char)('0' + rng.Next(10));
                    texts.Add(new string(m));
                    texts.Add(new string(m, 0, rng.Next(2, m.Length)));
                }

                foreach (string t in texts)
                {
                    cases++;
                    bool quick = PhoneNumber.TryParse(t, region, out PhoneNumber q);
                    OperationStatus status = PhoneNumber.TryParse(t, region, out PhoneNumber d, out _);
                    if (quick != (status == OperationStatus.Done))
                    {
                        differences.Add($"{region} '{t}': quick {quick} detailed {status}");
                        continue;
                    }

                    if (!quick)
                    {
                        continue;
                    }

                    if (q.E164 != d.E164 || q.Type != d.Type || q.IsValid != d.IsValid || q.Source != d.Source || q.Flags != d.Flags)
                    {
                        differences.Add($"{region} '{t}': quick {q.E164} {q.Type} {q.IsValid} detailed {d.E164} {d.Type} {d.IsValid}");
                    }

                    // Numbers made from parts never carry the parser's cached type, so Type and IsValid are computed from the plan.
                    PhoneNumber fresh = PhoneNumber.Create(q.CountryCode, q.NationalSignificantNumber);
                    if (fresh.Type != q.Type || fresh.IsValid != q.IsValid)
                    {
                        differences.Add($"{region} '{t}': cached {q.Type} {q.IsValid} computed {fresh.Type} {fresh.IsValid}");
                    }
                }
            }
        }

        Assert.True(cases > 3000);
        Assert.True(differences.Count == 0, $"{differences.Count} of {cases} differ:\n" + string.Join('\n', differences.Take(25)));
    }
}
