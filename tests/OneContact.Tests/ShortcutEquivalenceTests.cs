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

        string[] schemes = ["http://", "https://", "HTTP://", "Https://", "http:", "ftp://", "ws://", "wss://", "WSS://", "wsx://", "http://user@", "http://u:p@"];
        string[] hosts = ["example.com", "a.b.c.example.co.uk", "EXAMPLE.com", "localhost", "1.2.3.4", "xn--bcher-kva.example", "exa_mple.com", "a..b", "-a.com", "a-.com", "a.com.", "[::1]", "münchen.de", "a b.com", "0x7f.1", "9.9.9.9.9", "a%41.com", "e.com:80", "e.com:443", "e.com:8080", "e.com:99999", "e.com:", "e.com:0", "e.com:21", "e.com:00080", "Bücher.de", "x.xn--p1ai", "1.2.3", "0x7f.0.0.1", "a.b.0x1g", "%41.com", "e.com\ud800", "😀.com", "ex ample.com", "e.com\\x"];
        string[] paths = ["", "/", "/a", "/a/b/", "/a/./b", "/a/../b", "/a/%2e%2E/b", "/a b", "/a%20b", "/ä", "/a\\b", "//", "/a?b", "/~x/_y-z", "/a;b=c", "/a|b", "/a\"b", "/a{b}", "/a\\b/..\\c", "/%2e", "/%2E%2e/", "/a/b/..", "/.", "/..", "/a/%2E/b", "/a/.%2e/b", "/\U0001F600", "/\ud800x", "/a\u007fb", "/a\u0001b", "\\a", "\\\\", "/a b/../c d"];
        string[] queries = ["", "?", "?a=b", "?a=b&c=d", "?a=b c", "?a='b'", "?q=ä", "?a=%zz", "?a=\"x\"", "?a=<>", "?a=\U0001F600", "?a=\ud800", "?a b#c d"];
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
        for (int cp = 0; cp < IdnaTables.NfcStableLimit; cp++)
        {
            IdnaStatus fast = IdnaTables.GetStatus(cp, out ReadOnlySpan<char> fastMapping);
            IdnaStatus slow = IdnaTables.SearchStatus(cp, out ReadOnlySpan<char> slowMapping);
            Assert.True(fast == slow && fastMapping.SequenceEqual(slowMapping), $"U+{cp:X4}: {fast} '{fastMapping}' vs {slow} '{slowMapping}'");
        }
    }

    [Fact]
    public void Nfc_leaves_low_code_points_alone()
    {
        for (int cp = 0; cp < IdnaTables.NfcStableLimit; cp++)
        {
            Assert.Equal(0, IdnaTables.CombiningClass(cp));
            Assert.Equal([cp], NormalizationC.NormalizeFull([cp]));
        }

        // Pairs of them, too: nothing composes.
        int[] pair = new int[2];
        for (int a = 0; a < IdnaTables.NfcStableLimit; a++)
        {
            for (int b = 0; b < IdnaTables.NfcStableLimit; b += 7)
            {
                pair[0] = a;
                pair[1] = b;
                Assert.Equal(pair, NormalizationC.NormalizeFull(pair));
            }
        }
    }

    [Fact]
    public void Nfc_stable_code_points_are_left_alone_beside_each_other()
    {
        int[] neighbours = [0x61, 0x1100, 0x3042, 0x4F8B, 0x0915, 0xAC00 - 1, 0x1E9E];
        int stable = 0;
        int[] one = new int[1];
        int[] pair = new int[2];
        for (int cp = 0; cp <= 0x10FFFF; cp++)
        {
            if (cp is >= 0xD800 and <= 0xDFFF || !IdnaTables.IsNfcStable(cp))
            {
                continue;
            }

            stable++;
            one[0] = cp;
            Assert.True(NormalizationC.NormalizeFull(one).AsSpan().SequenceEqual(one), $"U+{cp:X4} alone");
            foreach (int n in neighbours)
            {
                if (!IdnaTables.IsNfcStable(n))
                {
                    continue;
                }

                pair[0] = n;
                pair[1] = cp;
                Assert.True(NormalizationC.NormalizeFull(pair).AsSpan().SequenceEqual(pair), $"U+{n:X4} U+{cp:X4}");
                pair[0] = cp;
                pair[1] = n;
                Assert.True(NormalizationC.NormalizeFull(pair).AsSpan().SequenceEqual(pair), $"U+{cp:X4} U+{n:X4}");
            }
        }

        Assert.True(stable > 100_000, $"only {stable} code points count as stable");
    }

    [Fact]
    public void Idna_property_answers_do_not_depend_on_what_was_asked_before()
    {
        // The code points collide in the small table that remembers answers; walking up, down and in strides must give the same answers.
        const int Count = 0x110000;
        var first = new int[Count];
        for (int cp = 0; cp < Count; cp++)
        {
            first[cp] = (IdnaTables.IsMark(cp) ? 1 : 0) | (IdnaTables.IsNfcStable(cp) ? 2 : 0) | ((int)IdnaTables.GetBidiClass(cp) << 2);
        }

        foreach (int stride in new[] { 1, 1023, 1024, 4099 })
        {
            for (int i = 0; i < Count; i++)
            {
                int cp = (int)(((long)i * stride + (stride == 1 ? Count - 1 - (2 * i) : 0)) % Count);
                if (cp < 0)
                {
                    cp += Count;
                }

                int now = (IdnaTables.GetBidiClass(cp) is var b ? (int)b << 2 : 0) | (IdnaTables.IsNfcStable(cp) ? 2 : 0) | (IdnaTables.IsMark(cp) ? 1 : 0);
                Assert.True(now == first[cp], $"U+{cp:X4}: {now} then {first[cp]}");
            }
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

        string[] locals = ["a", "john.smith", "J.Smith+crm", "a..b", ".a", "a.", "a b", "\"a b\"", "a(c)", "Ünï", "a!#$%&'*+-/=?^_`{|}~", "x@y", "", "a\u00a0", "1", "ユーザー", "名前.名", "a\ud800", "a\U0001F600", "é.é", "a\u0301", new string('a', 70)];
        string[] domains = ["example.com", "GoogleMail.com", "a-b.example", "-a.com", "a-.com", "a..com", "a.com.", ".com", "com", "1.2.3.4", "[1.2.3.4]", "münchen.de", "xn--bcher-kva.de", "a_b.com", "e.c", "example.123", "例え.jp", "bücher.de", "ünï.de", "a.é", "ÅÄ.COM", "xn--p1ai", "xn--.com", "a.\u0301b.com", "b\ud800.com", "d\U0001F600.com", "xn--bcher-kva.example", "STRASSE.de", "straße.de", "a.b\u200d", "münchen", "ü.ü", new string('b', 70) + ".com", "mailinator.com", "gmial.com", "x.co.uk"];
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

    // The ASCII-text route of ToASCII (labels checked in place, Punycode labels kept as written) against the general route.
    [Fact]
    public void Idna_ascii_text_route_equals_the_general_route()
    {
        Uts46Options[] optionSets =
        [
            Uts46Options.UseStd3AsciiRules | Uts46Options.CheckHyphens | Uts46Options.CheckBidi | Uts46Options.CheckJoiners,
            Uts46Options.CheckBidi | Uts46Options.CheckJoiners,
            Uts46Options.CheckBidi | Uts46Options.CheckJoiners | Uts46Options.VerifyDnsLength,
            Uts46Options.None,
            Uts46Options.CheckHyphens | Uts46Options.IgnoreInvalidPunycode | Uts46Options.VerifyDnsLength,
        ];
        var inputs = new HashSet<string>(StringComparer.Ordinal);
        var rng = new Random(20261106);
        const string alphabet = "abcdefghijklmnopqrstuvwxyz0123456789-";
        string RandomText(int length) => string.Create(length, rng, static (span, r) =>
        {
            for (int i = 0; i < span.Length; i++)
            {
                span[i] = alphabet[r.Next(alphabet.Length)];
            }
        });

        // Real A-labels: the general route encodes Unicode labels, and the encoded text is then an input in its own right.
        string[] unicode = ["bücher", "例え", "münchen", "буква", "יוניקוד", "مثال", "mañana", "☃", "aébèc", "नमस्ते"];
        foreach (string u in unicode)
        {
            Assert.True(Uts46.TryToAsciiGeneral(u, Uts46Options.None, out string a) || a.Length > 0);
            inputs.Add(a);
            inputs.Add(a.ToUpperInvariant());
            inputs.Add("info." + a + ".example");
            inputs.Add(a + "." + a);
            inputs.Add("a" + a);
        }

        (int Low, int High)[] ranges = [(0xE0, 0xFF), (0x430, 0x44F), (0x4E00, 0x4E50), (0x5D0, 0x5EA), (0x627, 0x64A), (0x61, 0x7A), (0x3042, 0x3093)];
        for (int n = 0; n < 600; n++)
        {
            var sb = new StringBuilder();
            (int low, int high) = ranges[rng.Next(ranges.Length)];
            for (int k = rng.Next(1, 7); k > 0; k--)
            {
                sb.Append(char.ConvertFromUtf32(rng.Next(3) == 0 ? ranges[rng.Next(ranges.Length)].Low : rng.Next(low, high + 1)));
            }

            if (Uts46.TryToAsciiGeneral(sb.ToString(), Uts46Options.None, out string encoded))
            {
                inputs.Add(encoded);
                inputs.Add("mail." + encoded + ".example");
                inputs.Add("mail." + encoded + ".xn--p1ai");
            }
        }

        for (int n = 0; n < 6000; n++)
        {
            string label = rng.Next(3) == 0 ? "xn--" + RandomText(rng.Next(1, 14)) : RandomText(rng.Next(0, 10));
            string tail = rng.Next(4) switch { 0 => "", 1 => ".example", 2 => ".com.", _ => "." + RandomText(rng.Next(1, 5)) };
            inputs.Add(label + tail);
            inputs.Add(RandomText(rng.Next(0, 6)) + "." + label + tail);
            if (rng.Next(20) == 0)
            {
                inputs.Add(new string('a', 62 + rng.Next(4)) + "." + label);
            }
        }

        foreach (string word in new[] { "", ".", "..", "-", "a-", "-a", "ab--cd", "xn--", "XN--", "xn---", "xn--a-", "xn--bcher-kva", "Xn--Bcher-Kva.Example", "EXAMPLE.COM", "a.b" })
        {
            inputs.Add(word);
        }

        int handled = 0;
        int accepted = 0;
        var differences = new List<string>();
        foreach (string input in inputs)
        {
            foreach (Uts46Options options in optionSets)
            {
                bool general = Uts46.TryToAsciiGeneral(input, options, out string expected);
                if (!Uts46.TryToAsciiOfAsciiText(input, options, out string actual, out bool ok))
                {
                    continue;
                }

                handled++;
                accepted += general ? 1 : 0;
                if (general != ok || (general && expected != actual))
                {
                    differences.Add($"'{input}' {options}: general {general} '{expected}', short {ok} '{actual}'");
                }
            }
        }

        Assert.True(handled > 20000, $"the short route was taken {handled} times");
        Assert.True(accepted > 1000, $"only {accepted} accepted inputs");
        Assert.True(differences.Count == 0, $"{differences.Count} of {handled} differ:\n" + string.Join('\n', differences.Take(20)));
    }

    [Fact]
    public void TheValidBmpRouteAnswersAsTheGeneralRouteDoes()
    {
        Uts46Options[] optionSets =
        [
            Uts46Options.UseStd3AsciiRules | Uts46Options.CheckHyphens | Uts46Options.CheckBidi | Uts46Options.CheckJoiners,
            Uts46Options.CheckBidi | Uts46Options.CheckJoiners,
            Uts46Options.CheckBidi | Uts46Options.CheckJoiners | Uts46Options.VerifyDnsLength,
            Uts46Options.None,
            Uts46Options.CheckHyphens | Uts46Options.VerifyDnsLength,
            Uts46Options.CheckJoiners | Uts46Options.UseStd3AsciiRules,
        ];
        var rng = new Random(20261107);

        // Latin with marks, Cyrillic, CJK, Hebrew and Arabic (right-to-left, declined), Hangul (NFC changes it, declined), full-width and
        // upper-case forms (mapped), ideographic full stop (maps to a dot), soft hyphen (ignored), joiners, ASCII, combining marks.
        (int Low, int High)[] ranges =
        [
            (0xE0, 0xFF), (0x430, 0x44F), (0x4E00, 0x4E50), (0x5D0, 0x5EA), (0x627, 0x64A), (0x61, 0x7A), (0x3042, 0x3093), (0x41, 0x5A),
            (0xFF21, 0xFF3A), (0xFF41, 0xFF5A), (0x300, 0x30F), (0xAC00, 0xAC20), (0x1100, 0x1112), (0x391, 0x3A9), (0x3B1, 0x3C9), (0x900, 0x93C),
        ];
        int[] singles = [0x3002, 0xAD, 0x200C, 0x200D, 0x2E, 0x2D, 0xDF, 0x3C2, 0xFF0E, 0x2460, 0x1E9E, 0x20];
        string[] tails = ["", ".example", ".com", ".", ".xn--p1ai", ".xn--bcher-kva", "." + new string('a', 64)];
        int handled = 0;
        int declined = 0;
        var differences = new List<string>();
        for (int n = 0; n < 12000; n++)
        {
            var sb = new StringBuilder();
            for (int labelCount = rng.Next(1, 4); labelCount > 0; labelCount--)
            {
                (int low, int high) = ranges[rng.Next(ranges.Length)];
                for (int k = rng.Next(1, 7); k > 0; k--)
                {
                    int cp = rng.Next(12) == 0 ? singles[rng.Next(singles.Length)] : rng.Next(5) == 0 ? ranges[rng.Next(ranges.Length)].Low : rng.Next(low, high + 1);
                    sb.Append(char.ConvertFromUtf32(cp));
                }

                if (labelCount > 1)
                {
                    sb.Append('.');
                }
            }

            sb.Append(tails[rng.Next(tails.Length)]);
            string input = sb.ToString();
            foreach (Uts46Options options in optionSets)
            {
                if (!Uts46.TryToAsciiOfValidBmpText(input, options, out string actual))
                {
                    declined++;
                    continue;
                }

                handled++;
                bool general = Uts46.TryToAsciiGeneral(input, options, out string expected);
                if (!general || expected != actual)
                {
                    differences.Add($"'{input}' {options}: general {general} '{expected}', short '{actual}'");
                }
            }
        }

        Assert.True(handled > 5000, $"the route was taken {handled} times and declined {declined}");
        Assert.True(differences.Count == 0, $"{differences.Count} of {handled} differ:\n" + string.Join('\n', differences.Take(20)));
    }
}
