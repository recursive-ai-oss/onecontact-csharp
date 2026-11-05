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

    [Fact]
    public void HostileShapesAreFast()
    {
        // Pathological inputs: long runs, many labels, many combining marks, many percent signs, deep dot segments.
        string[] hostile =
        [
            "https://" + new string('a', 200_000) + ".com/",
            "https://" + string.Concat(Enumerable.Repeat("a.", 50_000)) + "com/",
            "https://example." + new string('\u0301', 100_000) + "/",
            "https://xn--" + new string('a', 100_000) + ".com/",
            "https://example.com/" + string.Concat(Enumerable.Repeat("%", 200_000)),
            "https://example.com/" + string.Concat(Enumerable.Repeat("../", 100_000)),
            "mailto:" + new string(' ', 200_000) + "?" + new string('x', 200_000) + "#" + new string('y', 200_000),
            "https://" + string.Concat(Enumerable.Repeat("@", 100_000)) + "example.com",
            "http://" + string.Concat(Enumerable.Repeat("1.", 60_000)) + "1",
            string.Concat(Enumerable.Repeat("\ud800", 100_000)),
        ];
        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (string s in hostile)
        {
            _ = WebAddress.TryParse(s, out _);
        }

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20), "hostile inputs took " + sw.Elapsed);
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
