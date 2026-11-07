using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using RecursiveAI.OneContact.Addresses;
using RecursiveAI.OneContact.Addresses.Parsing;

namespace RecursiveAI.OneContact.Bench;

/// <summary>
/// Writes how the rule parser reads the first lines of every <c>*.dev.jsonl</c> register render in the given folders, once with the file's country
/// as the hint and once with no hint: status, country, facts, fields with offsets, canonical form, display form and key. Two builds are compared with
/// <c>cmp</c>. Run as <c>dump-address &lt;per-country count&gt; &lt;output file&gt; &lt;folder&gt;...</c>.
/// </summary>
internal static class AddressDump
{
    public static int Run(string[] args)
    {
        int per = int.Parse(args[0], CultureInfo.InvariantCulture);
        using var w = new StreamWriter(args[1], false, new UTF8Encoding(false)) { NewLine = "\n" };
        long cases = 0;
        foreach (string dir in args.Skip(2))
        {
            foreach (string file in Directory.GetFiles(dir, "*.dev.jsonl").Order(StringComparer.Ordinal))
            {
                string cc = Path.GetFileName(file)[..2].ToUpperInvariant();
                Region country = Region.Parse(cc);
                foreach (string line in File.ReadLines(file).Take(per))
                {
                    using JsonDocument doc = JsonDocument.Parse(line);
                    string text = doc.RootElement.GetProperty("text").GetString()!;
                    foreach (Region hint in new[] { country, Region.Unknown })
                    {
                        w.Write(cc);
                        w.Write(hint == Region.Unknown ? " none " : " hint ");
                        w.Write(text.Replace("\n", "\\n", StringComparison.Ordinal));
                        w.Write(" => ");
                        Write(w, text, hint);
                        cases++;
                    }
                }
            }
        }

        w.WriteLine($"cases {cases}");
        return 0;
    }

    private static void Write(StreamWriter w, string text, Region hint)
    {
        OperationStatus s = RuleAddressParser.TryParse(text, hint, out ParsedAddress? a, out ParseDetail d);
        if (s != OperationStatus.Done || a is null)
        {
            w.WriteLine($"{s} {d.PrimaryReason}");
            return;
        }

        w.Write($"{a.Country} {a.CountryConfidence:R} {a.CountryInferred} {a.Facts} {a.Span.Start}+{a.Span.Length}");
        foreach (ParsedAddressField f in a.Fields)
        {
            w.Write($" [{f.Name}|{f.Text}|{f.Standard}|{f.Span.Start}+{f.Span.Length}|{f.Confidence:R}|{f.Source}|{f.Rule}]");
        }

        foreach (ParsedAddressText l in a.LeadingLines)
        {
            w.Write($" lead[{l.Text}|{l.Standard}|{l.Span.Start}+{l.Span.Length}]");
        }

        PostalAddress p = PostalAddressParser.FromParsed(a);
        w.Write($" canon[{p.Canonical}] disp[{a.ToString(AddressStyle.Display, AddressLayout.Label, true)}] key[{p.Key}] issues[{string.Join('+', d.Issues.Select(i => i.Reason))}]");
        w.WriteLine();
    }
}
