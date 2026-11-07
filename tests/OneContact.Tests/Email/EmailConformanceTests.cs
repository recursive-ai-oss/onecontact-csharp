using System.Buffers;
using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace RecursiveAI.OneContact.Tests;

/// <summary>
/// Runs the committed conformance corpus (tests/OneContact.Tests/Email/corpus/*.cases: RFC 5322, RFC 3696, RFC 2047, RFC 6531/6532 and
/// the provider rules) and the isemail test list (BSD-3-Clause, pinned in upstream/isemail) through the email parser.
/// </summary>
public sealed class EmailConformanceTests
{
    private const string Separator = " \u00a6 ";

    private sealed record Case(string Id, string Source, string Input, string Expect, string Canonical, string Flags, string Display, string Key);

    internal static string Unescape(string s)
    {
        StringBuilder sb = new();
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] != '\\' || i + 1 >= s.Length)
            {
                sb.Append(s[i]);
                continue;
            }

            char n = s[++i];
            switch (n)
            {
                case 's': sb.Append(' '); break;
                case 't': sb.Append('\t'); break;
                case 'r': sb.Append('\r'); break;
                case 'n': sb.Append('\n'); break;
                case '\\': sb.Append('\\'); break;
                case 'u':
                    sb.Append((char)int.Parse(s.AsSpan(i + 1, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
                    i += 4;
                    break;
                default: sb.Append('\\').Append(n); break; // any other backslash is literal: quoted-pairs are written as they appear
            }
        }

        return sb.ToString();
    }

    // The corpus is read from the source tree, like the other test data that is located from the test file.
    internal static string CorpusDirectory([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, "corpus");

    private static List<Case> Load(string file)
    {
        List<Case> cases = [];
        foreach (string line in File.ReadAllLines(Path.Combine(CorpusDirectory(), file), Encoding.UTF8))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] c = line.Split(Separator);
            string At(int i) => i < c.Length ? c[i] : "*";
            cases.Add(new Case(c[0], c[1], Unescape(c[2]), c[3], At(4), At(5), At(6), At(7)));
        }

        return cases;
    }

    private static string FlagsText(EmailFacts f)
    {
        // The flags that are findings about the text; the list-based flags are included because the corpus pins them too.
        List<string> names = [];
        foreach (EmailFacts v in Enum.GetValues<EmailFacts>())
        {
            if (v != EmailFacts.None && (f & v) == v)
            {
                names.Add(v.ToString());
            }
        }

        names.Sort(StringComparer.Ordinal);
        return names.Count == 0 ? "-" : string.Join('|', names);
    }

    private static string Check(Case c)
    {
        OperationStatus status = EmailParserApi(c.Input, out EmailAddress value, out ParseDetail detail);
        if (c.Expect == "valid")
        {
            if (status != OperationStatus.Done)
            {
                return $"{c.Id}: expected valid, got {detail.PrimaryReason.Id}";
            }

            if (c.Canonical != "*" && value.Canonical != Unescape(c.Canonical))
            {
                return $"{c.Id}: canonical '{value.Canonical}' != '{Unescape(c.Canonical)}'";
            }

            if (c.Flags != "*")
            {
                string expected = string.Join('|', c.Flags.Split('|').OrderBy(x => x, StringComparer.Ordinal));
                if (FlagsText(value.Flags) != expected)
                {
                    return $"{c.Id}: flags {FlagsText(value.Flags)} != {expected}";
                }
            }

            if (c.Display != "*" && value.DisplayName != (c.Display == "-" ? null : Unescape(c.Display)))
            {
                return $"{c.Id}: display '{value.DisplayName}' != '{c.Display}'";
            }

            if (c.Key != "*" && value.Key != Unescape(c.Key))
            {
                return $"{c.Id}: key '{value.Key}' != '{Unescape(c.Key)}'";
            }

            // The canonical form parses back to itself, and the key is stable under a second pass.
            if (!EmailAddress.TryParse(value.Canonical, out EmailAddress again) || again.Canonical != value.Canonical)
            {
                return $"{c.Id}: canonical '{value.Canonical}' does not parse back to itself";
            }

            if (EmailAddress.TryParse(value.Key, out EmailAddress keyAgain) && keyAgain.Key != value.Key)
            {
                return $"{c.Id}: key '{value.Key}' is not stable: '{keyAgain.Key}'";
            }

            return string.Empty;
        }

        if (status == OperationStatus.Done)
        {
            return $"{c.Id}: expected {c.Expect}, but it parsed to '{value.Canonical}'";
        }

        if (c.Expect.StartsWith("invalid:", StringComparison.Ordinal) && detail.PrimaryReason.Id != c.Expect[8..])
        {
            return $"{c.Id}: reason {detail.PrimaryReason.Id} != {c.Expect[8..]}";
        }

        return string.Empty;
    }

    private static OperationStatus EmailParserApi(string input, out EmailAddress value, out ParseDetail detail) =>
        Email.EmailParser.TryParse(input, out value, out detail);

    [Fact]
    public void Corpus_cases_all_agree()
    {
        int total = 0;
        List<string> failures = [];
        foreach (string file in new[] { "rfc5322.cases", "rfc3696-2047-6531.cases", "rules.cases" })
        {
            foreach (Case c in Load(file))
            {
                total++;
                string result = Check(c);
                if (result.Length > 0)
                {
                    failures.Add(result);
                }
            }
        }

        WriteSummary("corpus", total, failures.Count);
        Assert.True(failures.Count == 0, $"{failures.Count} of {total} corpus cases fail:\n{string.Join('\n', failures.Take(40))}");
    }

    private static string Expected(string category) => category == "ISEMAIL_ERR" ? "invalid" : "valid";

    [Fact]
    public void Isemail_list_agrees_with_the_rfc_reading()
    {
        string path = UpstreamFiles.Get("isemail", "test/tests.xml");
        Dictionary<string, (string Expect, string Why)> overrides = [];
        foreach (string line in File.ReadAllLines(Path.Combine(CorpusDirectory(), "isemail-overrides.cases"), Encoding.UTF8))
        {
            if (line.Length > 0 && line[0] != '#')
            {
                string[] c = line.Split(Separator);
                overrides[c[0]] = (c[1], c[2]);
            }
        }

        int total = 0;
        int overridden = 0;
        List<string> failures = [];
        foreach (XElement test in XDocument.Load(path).Descendants("test"))
        {
            string id = (string)test.Attribute("id")!;
            string address = test.Element("address")?.Value ?? string.Empty;

            // The list writes ASCII control characters as the Unicode "control picture" characters U+2400 + code (its note 1).
            StringBuilder sb = new();
            foreach (char ch in address)
            {
                sb.Append(ch is >= '\u2400' and <= '\u241F' ? (char)(ch - 0x2400) : ch == '\u2421' ? '\u007f' : ch);
            }

            string input = sb.ToString();
            string category = test.Element("category")!.Value;
            string expect = Expected(category);
            if (overrides.TryGetValue(id, out (string Expect, string Why) o))
            {
                expect = o.Expect;
                overridden++;
            }

            total++;
            bool ok = EmailAddress.TryParse(input, out EmailAddress value);
            if (ok != (expect == "valid"))
            {
                OperationStatus status = EmailParserApi(input, out _, out ParseDetail detail);
                failures.Add($"isemail {id} '{address}' ({test.Element("diagnosis")!.Value}): expected {expect}, parsed={ok} {(status == OperationStatus.Done ? value.Canonical : detail.PrimaryReason.Id)}");
            }
        }

        WriteSummary("isemail", total, failures.Count, overridden);
        Assert.True(failures.Count == 0, $"{failures.Count} of {total} isemail cases differ:\n{string.Join('\n', failures.Take(80))}");
    }

    private static void WriteSummary(string name, int total, int failed, int overridden = 0)
    {
        string? dir = Environment.GetEnvironmentVariable("ONECONTACT_TEST_SUMMARY");
        if (!string.IsNullOrEmpty(dir))
        {
            File.AppendAllText(Path.Combine(dir, "email-conformance.txt"), $"{name}: total {total}, failed {failed}, expectation overrides {overridden}\n");
        }
    }
}
