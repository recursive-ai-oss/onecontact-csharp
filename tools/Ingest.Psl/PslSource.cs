using System.Text;
using RecursiveAI.OneContact.Text.Idna;

namespace RecursiveAI.OneContact.Ingest.Psl;

/// <summary>
/// Compiles <c>public_suffix_list.dat</c> into the table <c>rules</c>: one rule per line, in ordinal order, as
/// <c>[N|W|E][I|P]&lt;rule&gt;</c>. N is a plain rule, W a wildcard rule (<c>*.rule</c>, stored without the star), E an exception
/// rule (<c>!rule</c>, stored without the bang); I and P say whether the rule is in the ICANN or the private section. Labels that
/// are not ASCII are stored as their <c>xn--</c> form, so a lookup only ever compares ASCII. The list itself is shipped untouched
/// next to the compiled table (see docs/psl.md). Test file <c>tests/test_psl.txt</c> is a pinned input too but is not compiled.
/// </summary>
public sealed class PslSource : IIngestSource
{
    /// <inheritdoc/>
    public string Name => "psl";

    /// <inheritdoc/>
    public string ToolVersion => "1";

    /// <inheritdoc/>
    public string NoticeText =>
        "The Public Suffix List (https://publicsuffix.org/list/), shipped unmodified as public_suffix_list.dat.\n" +
        "This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.\n" +
        "The unmodified file is in the package under notices/; the compiled rule table is derived from it, and additions of ours are in a separate file.\n";

    /// <inheritdoc/>
    public void Convert(IngestInput input, TableSetWriter tables)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(tables);
        string text = Encoding.UTF8.GetString(input.ReadAllBytes("public_suffix_list.dat"));
        var rules = new SortedSet<string>(StringComparer.Ordinal);
        char section = 'P';
        bool sawIcann = false;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');

            // The list marks its sections with comment lines; a rule outside both belongs to neither, which does not occur.
            if (line.StartsWith("// ===BEGIN ICANN DOMAINS===", StringComparison.Ordinal))
            {
                section = 'I';
                sawIcann = true;
                continue;
            }

            if (line.StartsWith("// ===BEGIN PRIVATE DOMAINS===", StringComparison.Ordinal))
            {
                section = 'P';
                continue;
            }

            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            // The list says each line is read only up to the first white space.
            int space = line.IndexOfAny([' ', '\t']);
            string rule = space >= 0 ? line[..space] : line;
            char type = 'N';
            if (rule.StartsWith('!'))
            {
                type = 'E';
                rule = rule[1..];
            }
            else if (rule.StartsWith("*.", StringComparison.Ordinal))
            {
                type = 'W';
                rule = rule[2..];
            }

            if (rule.Contains('*', StringComparison.Ordinal) || rule.Length == 0)
            {
                throw new InvalidDataException("Unsupported rule shape: " + line);
            }

            rules.Add(string.Concat(type, section, ToAscii(rule)));
        }

        if (!sawIcann || rules.Count < 1000)
        {
            throw new InvalidDataException("The list has no ICANN section or implausibly few rules.");
        }

        tables.Add("rules", Encoding.UTF8.GetBytes(string.Join('\n', rules)));
    }

    private static string ToAscii(string rule)
    {
        var parts = new List<string>();
        foreach (string label in rule.Split('.'))
        {
            bool ascii = true;
            foreach (char c in label)
            {
                ascii &= c < 0x80;
            }

            if (ascii)
            {
                parts.Add(label);
                continue;
            }

            var cps = new List<int>();
            for (int i = 0; i < label.Length; i++)
            {
                int cp = char.ConvertToUtf32(label, i);
                if (char.IsHighSurrogate(label[i]))
                {
                    i++;
                }

                cps.Add(cp);
            }

            if (!Punycode.TryEncode(cps.ToArray(), out string encoded))
            {
                throw new InvalidDataException("Cannot encode rule label: " + label);
            }

            parts.Add("xn--" + encoded);
        }

        return string.Join('.', parts);
    }
}
