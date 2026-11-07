using System.Globalization;
using System.Text;
using RecursiveAI.OneContact.Extraction;
using RecursiveAI.OneContact.Extraction.Records;
using RecursiveAI.OneContact.Phones;

namespace RecursiveAI.OneContact.Bench;

/// <summary>
/// Writes what the extractor, the phone matcher and the email and web parsers answer for a fixed set of inputs, one line per fact, so
/// two builds can be compared with <c>cmp</c>. Run as <c>dump-diff &lt;extraction fixtures folder&gt; &lt;output file&gt;</c>.
/// </summary>
internal static class DiffDump
{
    public static int Run(string fixtures, string output)
    {
        using var w = new StreamWriter(output, false, new UTF8Encoding(false)) { NewLine = "\n" };
        int pages = 0;

        // Generated pages: varied seeds and sizes, as HTML and as text, with and without a default region.
        for (int seed = 1; seed <= 40; seed++)
        {
            Inputs.Page p = Inputs.MakePage(seed, 3 + (seed % 28));
            foreach (Region region in new[] { Region.US, Region.Unknown })
            {
                var extractor = new ContactExtractor { DefaultRegion = region };
                Write(w, $"gen-html-{seed}-{region}", extractor.ExtractHtml(p.Html));
                Write(w, $"gen-text-{seed}-{region}", extractor.ExtractText(p.Text));
                pages += 2;
            }
        }

        // The extraction fixtures of the test suite.
        foreach (string file in Directory.EnumerateFiles(fixtures, "*.html", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            string html = File.ReadAllText(file);
            foreach (Region region in new[] { Region.US, Region.Unknown })
            {
                Write(w, $"{Path.GetRelativePath(fixtures, file)}-{region}", new ContactExtractor { DefaultRegion = region }.ExtractHtml(html));
                pages++;
            }
        }

        foreach (string file in Directory.EnumerateFiles(Path.Combine(fixtures, "text", "notes"), "*.txt").Order(StringComparer.Ordinal))
        {
            string text = File.ReadAllText(file);
            foreach (Region region in new[] { Region.US, Region.Unknown })
            {
                Write(w, $"{Path.GetFileName(file)}-{region}", new ContactExtractor { DefaultRegion = region }.ExtractText(text));
                pages++;
            }
        }

        // The phone matcher over the one-megabyte text, at every leniency.
        string phoneText = PhoneMatcherRow.MakeText();
        foreach (PhoneMatchLeniency leniency in Enum.GetValues<PhoneMatchLeniency>())
        {
            int n = 0;
            foreach (PhoneNumberMatch m in PhoneNumber.FindAll(phoneText, Region.US, leniency))
            {
                w.WriteLine(string.Create(CultureInfo.InvariantCulture, $"match {leniency} {m.Start} {m.RawString.Length} {m.Number.E164} {m.Number.Key}"));
                n++;
            }

            w.WriteLine($"matches {leniency} {n}");
        }

        // Internationalized and ordinary domains through the email and web parsers.
        var rng = new Random(20261107);
        (int Low, int High)[] ranges = [(0xE0, 0xFF), (0x430, 0x44F), (0x4E00, 0x4E50), (0x5D0, 0x5EA), (0x627, 0x64A), (0x61, 0x7A), (0x3042, 0x3093), (0x41, 0x5A), (0xFF21, 0xFF3A), (0x300, 0x30F), (0xAC00, 0xAC20), (0x391, 0x3A9), (0x900, 0x93C)];
        string[] tails = ["", ".example", ".com", ".jp", ".xn--p1ai", ".xn--bcher-kva", ".co.uk", "."];
        for (int i = 0; i < 20000; i++)
        {
            var sb = new StringBuilder();
            for (int labels = rng.Next(1, 3); labels > 0; labels--)
            {
                (int low, int high) = ranges[rng.Next(ranges.Length)];
                for (int k = rng.Next(1, 6); k > 0; k--)
                {
                    sb.Append(char.ConvertFromUtf32(rng.Next(6) == 0 ? ranges[rng.Next(ranges.Length)].Low : rng.Next(low, high + 1)));
                }

                sb.Append('.');
            }

            sb.Length--;
            sb.Append(tails[rng.Next(tails.Length)]);
            string domain = sb.ToString();
            bool mail = EmailAddress.TryParse("user@" + domain, out EmailAddress e);
            w.WriteLine($"idn-email {domain} {mail} {(mail ? e.Domain + " " + e.Key : string.Empty)}");
            bool web = WebAddress.TryParse("https://" + domain + "/x", out WebAddress u);
            w.WriteLine($"idn-web {domain} {web} {(web ? u.Canonical + " " + u.Key : string.Empty)}");
        }

        foreach ((string text, _) in Inputs.Emails.Concat(Inputs.EmailsIdn))
        {
            bool ok = EmailAddress.TryParse(text, out EmailAddress a);
            w.WriteLine($"email {text} {ok} {(ok ? a.Canonical + " " + a.Key + " " + a.Flags : string.Empty)}");
        }

        w.WriteLine($"pages {pages}");
        return 0;
    }

    private static void Write(StreamWriter w, string name, ExtractResult r)
    {
        w.WriteLine($"== {name} unit {r.OffsetUnit} fields {r.Fields.Count} records {r.Records.Count} issues {r.Issues.Count}");
        foreach (ExtractedField f in r.Fields)
        {
            w.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"f {f.Id} {f.Kind} {f.Source} [{f.Raw}] [{f.Text}] {f.Span.Start}+{f.Span.Length} {f.HasSpan} {f.Flags} c={f.Confidence:R} n={f.Normalized.Kind} k={f.Normalized.Key} s={f.Normalized} i={f.Issue} facts={string.Join(',', f.Facts)} rec={f.RecordId} blk={f.BlockId} dec={f.Decision} dup={f.Duplicates.Count} label={f.Label}"));
        }

        foreach (ContactRecord rec in r.Records)
        {
            w.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"r {rec.Id} {rec.Kind} {rec.Source} {rec.Span.Start}+{rec.Span.Length} c={rec.Confidence:R} fields={string.Join(',', rec.Fields.Select(x => x.Id))} links={string.Join(',', rec.Links.Select(x => x.Kind + ":" + x.Target.Id))} dis={rec.Disagreements.Count}"));
        }

        foreach (ExtractIssue issue in r.Issues)
        {
            w.WriteLine($"issue {issue}");
        }
    }
}
