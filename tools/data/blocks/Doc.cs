using System.Text;

namespace OneContact.Data.Blocks;

/// <summary>One labelled span. <see cref="Mode"/> says how the shown text relates to the source value: empty = identical, 'u' = upper-cased,
/// 'n' = a space replaced by a line break, 'c' = first letter capitalised (email), 'o' = an obfuscated spelling of an email address or a spaced-out phone number.</summary>
internal sealed record SpanRec(int Start, int End, string Label, int Record, string Mode, string Expected);

/// <summary>Builds a block's text and its spans together, so the offsets are right by construction, and checks them afterwards against the source values.</summary>
internal sealed class Doc
{
    private readonly StringBuilder _text = new();
    private bool _first = true;

    internal readonly List<SpanRec> Spans = [];
    internal readonly List<int> Bounds = [];
    internal int Record = -1;

    /// <summary>True for the page view (visible-text lines); false for the CRM/UGC view.</summary>
    internal bool Page;

    /// <summary>In the CRM view, what joins items. "\n" keeps one item per line.</summary>
    internal string Sep = "\n";

    /// <summary>Names, organisations and addresses are shown in capitals.</summary>
    internal bool Upper;

    internal Prng R = null!;

    internal string Text => _text.ToString();

    internal int Position => _text.Length;

    internal bool Lines => Page || Sep == "\n";

    /// <summary>Starts a new item: a line in the page view, a separator in the CRM view.</summary>
    internal Doc Next()
    {
        if (_first)
        {
            _first = false;
            return this;
        }

        _text.Append(Lines ? "\n" : Sep);
        return this;
    }

    /// <summary>Starts an item on a new line in both views (the members of a staff list, the entries of a location list).</summary>
    internal Doc NextGroup()
    {
        if (_first)
        {
            _first = false;
            return this;
        }

        _text.Append('\n');
        return this;
    }

    /// <summary>Starts a record (a person or organisation's block) at the current position.</summary>
    internal Doc StartRecord()
    {
        Record++;
        Bounds.Add(_text.Length);
        return this;
    }

    /// <summary>Unlabelled text: labels, separators, filler.</summary>
    internal Doc Raw(string s)
    {
        _text.Append(s);
        return this;
    }

    /// <summary>A line of unlabelled text that is kept in both views (page chrome, prose around a block).</summary>
    internal Doc Line(string s)
    {
        Next();
        _text.Append(s);
        return this;
    }

    /// <summary>An item that carries no field (navigation, boilerplate, filler). Dropped in the CRM view unless items sit on their own lines.</summary>
    internal Doc Noise(string s)
    {
        if (Lines)
        {
            Next();
            _text.Append(s);
        }

        return this;
    }

    internal static string UpperSafe(string s)
    {
        string u = s.ToUpperInvariant();
        return u.Length == s.Length ? u : s;
    }

    /// <summary>Appends a labelled field. <paramref name="source"/> is the value as the source gave it; the shown text may differ by
    /// the documented mode only.</summary>
    internal Doc Field(string label, string source, bool name = false, string? shown = null, string mode = "")
    {
        string text = shown ?? source;
        if (name && Upper && mode.Length == 0)
        {
            string u = UpperSafe(text);
            if (u != text)
            {
                text = u;
                mode = "u";
            }
        }

        if (name && Page && mode.IndexOf('o', StringComparison.Ordinal) < 0 && R.Chance(0.02))
        {
            int sp = text.IndexOf(' ', StringComparison.Ordinal);
            if (sp > 0 && sp < text.Length - 1)
            {
                text = string.Concat(text.AsSpan(0, sp), "\n", text.AsSpan(sp + 1));
                mode += "n";
            }
        }

        int start = _text.Length;
        _text.Append(text);
        Spans.Add(new SpanRec(start, _text.Length, label, Record, mode, source));
        return this;
    }

    /// <summary>The text of a raw label in capitals when the block is.</summary>
    internal string Case(string s) => Upper ? UpperSafe(s) : s;

    /// <summary>Checks every span against its source value. Returns the number of failures.</summary>
    internal int Check(out string? firstProblem)
    {
        string text = _text.ToString();
        int bad = 0;
        firstProblem = null;
        foreach (SpanRec s in Spans)
        {
            bool ok = s.Start >= 0 && s.End <= text.Length && s.Start < s.End;
            if (ok)
            {
                string shown = text.Substring(s.Start, s.End - s.Start);
                ok = Matches(shown, s);
            }

            if (!ok)
            {
                bad++;
                firstProblem ??= $"{s.Label} {s.Start}-{s.End} mode '{s.Mode}' source '{s.Expected}'";
            }
        }

        for (int i = 0; i < Bounds.Count; i++)
        {
            if (Bounds[i] > text.Length || i > 0 && Bounds[i] <= Bounds[i - 1])
            {
                bad++;
                firstProblem ??= "record boundary out of order";
            }
        }

        return bad;
    }

    private static bool Matches(string shown, SpanRec s)
    {
        string expected = s.Expected;
        if (s.Mode.Contains('o', StringComparison.Ordinal))
        {
            return s.Label == "email" ? Obfuscation.DecodeEmail(shown).Equals(expected, StringComparison.OrdinalIgnoreCase)
                                      : Obfuscation.Digits(shown) == expected;
        }

        if (s.Mode.Contains('n', StringComparison.Ordinal))
        {
            shown = shown.Replace('\n', ' ');
            expected = expected.Replace('\n', ' ');
        }

        if (s.Mode.Contains('u', StringComparison.Ordinal))
        {
            expected = UpperSafe(expected);
        }

        return string.Equals(shown, expected, s.Mode.Contains('c', StringComparison.Ordinal) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}

/// <summary>How obfuscated spellings are turned back into the value they stand for (used only to check spans).</summary>
internal static class Obfuscation
{
    internal static readonly string[] At = ["[at]", "(at)", "{at}", " at "];
    internal static readonly string[] Dot = ["[dot]", "(dot)", "{dot}", " dot "];

    internal static string DecodeEmail(string s)
    {
        foreach (string a in At)
        {
            s = s.Replace(a, "@", StringComparison.OrdinalIgnoreCase);
        }

        foreach (string d in Dot)
        {
            s = s.Replace(d, ".", StringComparison.OrdinalIgnoreCase);
        }

        return s.Replace(" ", "", StringComparison.Ordinal);
    }

    internal static string Digits(string s)
    {
        StringBuilder b = new();
        foreach (char c in s)
        {
            if (c is >= '0' and <= '9')
            {
                b.Append(c);
            }
        }

        return b.ToString();
    }
}
