using System.Text;

namespace RecursiveAI.OneContact.Tests;

/// <summary>
/// A short smoke fuzz of the web address parser: seeded mutations of URL-like text, with the properties that must hold for every input.
/// Nothing throws but <see cref="ContactParseException"/>, and what parses is stable: parsing the canonical form again gives the same
/// canonical form and key.
/// </summary>
public class WebFuzzSmokeTests
{
    private static readonly string[] Seeds =
    [
        "https://example.com/", "http://user:pw@Example.COM:8080/a/./b/../c?x=1&utm_source=y#frag", "www.example.co.uk/path", "//host/path",
        "file:///C:/Windows/../x", "mailto:a@b.example?subject=x", "javascript:alert(1)", "http://[2001:db8::1]:80/", "http://0x7f.1/",
        "https://xn--bcher-kva.example/", "https://münchen.de/straße", "http://a/%zz%41?%#%", "foo://bar baz/ qux", "https://example.com:99999/",
        "https://\u202e.example/", "https://a.b.c.d.e.f.g.h.i.j.k.l.m.n.o.p.q.r.s.t.u.v.w.x.y.z.github.io/", "data:text/plain,hi there",
        "http://1.2.3.4.5/", "http://[::ffff:1.2.3.4]/", "https://example.com/?a=1&&b&=c&gclid=1&fbclid=2",
    ];

    private static readonly string Alphabet = "/\\?#@:[]%.&=;~!$'()*+, \t\n-_{}|^`\"<>\u00e9\u00df\u0301\u200d\u05d0\u0627\u4e2d\ud83d\ude00\ufffd0123456789abcdefxXnN";

    [Fact]
    public void ParserNeverThrowsAndCanonicalFormIsStable()
    {
        var random = new Random(20261105);
        int parsed = 0;
        for (int i = 0; i < 60_000; i++)
        {
            string input = Mutate(random);
            WebAddress w;
            try
            {
                if (!WebAddress.TryParse(input, out w))
                {
                    continue;
                }

                Contacts.TryNormalize(input, Region.US, out _);
            }
            catch (Exception e) when (e is not ContactParseException)
            {
                Assert.Fail($"{WptUrlTests.Show(input)} threw {e.GetType().Name}: {e.Message}");
                return;
            }

            parsed++;
            WebAddress again = WebAddress.Parse(w.Canonical);
            Assert.True(w.Canonical == again.Canonical, $"{WptUrlTests.Show(input)} -> {WptUrlTests.Show(w.Canonical)} -> {WptUrlTests.Show(again.Canonical)}");
            Assert.True(w.Key == again.Key, $"{WptUrlTests.Show(input)}: key {WptUrlTests.Show(w.Key)} vs {WptUrlTests.Show(again.Key)}");
        }

        Assert.True(parsed > 4_000, "the mutations should leave plenty of valid inputs, got " + parsed);
    }

    // The guard is the growth rate, not the clock: each hostile shape is parsed at n and at 4n repeats, taking the best of several runs so that a busy
    // machine cannot fail it, and the larger run may cost at most 10 times the smaller (linear work is about 4, quadratic work is 16).
    [Fact]
    public void HostileShapesGrowLinearly()
    {
        // Pathological inputs: long runs, many labels, many combining marks, many percent signs, deep dot segments.
        (string Name, Func<int, string> Build)[] shapes =
        [
            ("long label", n => "https://" + new string('a', n) + ".com/"),
            ("many labels", n => "https://" + string.Concat(Enumerable.Repeat("a.", n / 4)) + "com/"),
            ("combining marks", n => "https://example." + new string('\u0301', n / 2) + "/"),
            ("punycode run", n => "https://xn--" + new string('a', n / 2) + ".com/"),
            ("percent signs", n => "https://example.com/" + new string('%', n)),
            ("dot segments", n => "https://example.com/" + string.Concat(Enumerable.Repeat("../", n / 2))),
            ("opaque path, query, fragment", n => "mailto:" + new string(' ', n) + "?" + new string('x', n) + "#" + new string('y', n)),
            ("at signs", n => "https://" + new string('@', n / 2) + "example.com"),
            ("ipv4 parts", n => "http://" + string.Concat(Enumerable.Repeat("1.", n / 3)) + "1"),
            ("lone surrogates", n => new string('\ud800', n / 2)),
        ];
        foreach ((string name, Func<int, string> build) in shapes)
        {
            double small = Best(build(25_000));
            double large = Best(build(100_000));
            Assert.True(large <= 10 * Math.Max(small, 2.0), $"{name}: {small:F1} ms at n, {large:F1} ms at 4n");
        }
    }

    // Best of five runs, in milliseconds.
    private static double Best(string input)
    {
        double best = double.MaxValue;
        for (int i = 0; i < 5; i++)
        {
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            _ = WebAddress.TryParse(input, out _);
            best = Math.Min(best, System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }

        return best;
    }

    private static string Mutate(Random r)
    {
        var sb = new StringBuilder(Seeds[r.Next(Seeds.Length)]);
        int edits = r.Next(0, 6);
        for (int e = 0; e < edits; e++)
        {
            int pos = r.Next(sb.Length + 1);
            switch (r.Next(4))
            {
                case 0:
                    sb.Insert(pos, Alphabet[r.Next(Alphabet.Length)]);
                    break;
                case 1 when sb.Length > 0:
                    sb.Remove(Math.Min(pos, sb.Length - 1), 1);
                    break;
                case 2 when sb.Length > 0:
                    sb[Math.Min(pos, sb.Length - 1)] = Alphabet[r.Next(Alphabet.Length)];
                    break;
                default:
                    string donor = Seeds[r.Next(Seeds.Length)];
                    sb.Insert(pos, donor[..r.Next(donor.Length)]);
                    break;
            }
        }

        return sb.ToString();
    }
}
