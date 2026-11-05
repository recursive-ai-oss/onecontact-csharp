using System.Text;
using RecursiveAI.OneContact.Extraction;
using RecursiveAI.OneContact.Extraction.Records;

namespace RecursiveAI.OneContact.Tests;

/// <summary>The synthetic extraction pages and a plain-text rendering of a result, which the golden files hold.</summary>
internal static class ExtractionFixtures
{
    internal static string Root => Path.Combine(AppContext.BaseDirectory, "testdata", "extraction");

    internal static IEnumerable<string> PageFiles() =>
        Directory.EnumerateFiles(Path.Combine(Root, "pages"), "*.html").Order(StringComparer.Ordinal);

    /// <summary>The HTML pages that exercise the visible-text rules.</summary>
    internal static IEnumerable<string> TextPageFiles() =>
        Directory.EnumerateFiles(Path.Combine(Root, "text", "pages"), "*.html").Order(StringComparer.Ordinal);

    /// <summary>The plain-text notes (CRM and comment strings) that exercise the text rules.</summary>
    internal static IEnumerable<string> NoteFiles() =>
        Directory.EnumerateFiles(Path.Combine(Root, "text", "notes"), "*.txt").Order(StringComparer.Ordinal);

    internal static string NameOf(string pageFile) => Path.GetFileNameWithoutExtension(pageFile);

    /// <summary>The encoding a fixture is written in, by its name: <c>.cp1252</c> and <c>.utf16le</c> mark the others; the rest are UTF-8.</summary>
    internal static Encoding EncodingOf(string pageFile)
    {
        string name = NameOf(pageFile);
        if (name.EndsWith(".cp1252", StringComparison.Ordinal))
        {
            System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return System.Text.Encoding.GetEncoding(1252);
        }

        return name.EndsWith(".utf16le", StringComparison.Ordinal) ? new UnicodeEncoding(false, false) : new UTF8Encoding(false);
    }

    /// <summary>Escapes control characters, quotes and backslashes so a value fits on one line of a golden file.</summary>
    internal static string Escape(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s)
        {
            sb.Append(c switch
            {
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                '"' => "\\\"",
                '\\' => "\\\\",
                _ when c < 0x20 => $"\\u{(int)c:x4}",
                _ => c.ToString(),
            });
        }

        return sb.ToString();
    }

    /// <summary>One line per fact about a result: stable, readable and complete enough that a golden file shows every field.</summary>
    internal static string Dump(ExtractResult result)
    {
        var sb = new StringBuilder();
        sb.Append("unit ").Append(result.OffsetUnit).Append('\n');
        foreach (ContactRecord r in result.Records)
        {
            sb.Append("record ").Append(r.Id).Append(' ').Append(r.Kind).Append(" src=").Append(r.Source)
              .Append(" conf=").Append(r.Confidence.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))
              .Append(r.HasSpan ? $" span={r.Span.Start}+{r.Span.Length}" : " span=none").Append('\n');
            foreach (ExtractedField f in r.Fields)
            {
                Field(sb, f, "  ");
            }

            foreach (RecordLink l in r.Links)
            {
                sb.Append("  link ").Append(l.Kind).Append(" -> record ").Append(l.Target.Id).Append('\n');
            }

            foreach (FieldDisagreement d in r.Disagreements)
            {
                sb.Append("  disagreement ").Append(d.Kind).Append(": ").Append(string.Join(", ", d.Fields.Select(f => "#" + f.Id))).Append('\n');
            }
        }

        foreach (ExtractedField f in result.Ungrouped)
        {
            Field(sb, f, "ungrouped ");
        }

        foreach (ExtractIssue i in result.Issues)
        {
            sb.Append("issue ").Append(i.Kind).Append(' ').Append(i.Source).Append(" span=").Append(i.Span.Start).Append('+').Append(i.Span.Length).Append('\n');
        }

        return sb.ToString();
    }

    private static void Field(StringBuilder sb, ExtractedField f, string indent)
    {
        sb.Append(indent).Append('#').Append(f.Id).Append(' ').Append(f.Kind).Append(' ').Append(f.Source)
          .Append(" raw=\"").Append(Escape(f.Raw)).Append("\" text=\"").Append(Escape(f.Text)).Append('"')
          .Append(f.HasSpan ? $" span={f.Span.Start}+{f.Span.Length}" : " span=none")
          .Append(" conf=").Append(f.Confidence.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
        if (f.Flags != ExtractedFieldFacts.None)
        {
            sb.Append(" flags=").Append(f.Flags.ToString().Replace(", ", "|", StringComparison.Ordinal));
        }

        if (f.IsNormalized)
        {
            sb.Append(" => ").Append(f.Normalized.Kind).Append(':').Append(Escape(f.Normalized.Canonical));
        }

        if (f.Facts.Count > 0)
        {
            sb.Append(" facts=").Append(string.Join('|', f.Facts));
        }

        if (!f.Issue.IsNone)
        {
            sb.Append(" issue=").Append(f.Issue.Id);
        }

        if (f.Label is not null)
        {
            sb.Append(" label=\"").Append(Escape(f.Label)).Append('"');
        }

        if (f.BlockId >= 0)
        {
            sb.Append(" block=").Append(f.BlockId);
        }

        sb.Append('\n');
        foreach (ExtractedField d in f.Duplicates)
        {
            sb.Append(indent).Append("  also ").Append(d.Source).Append(" span=").Append(d.Span.Start).Append('+').Append(d.Span.Length).Append('\n');
        }
    }
}
