using RecursiveAI.OneContact.Extraction.Markup;
using RecursiveAI.OneContact.Extraction.Records;
using RecursiveAI.OneContact.Models;
using RecursiveAI.OneContact.Web;

namespace RecursiveAI.OneContact.Extraction.Text;

/// <summary>
/// The model half of the visible-text reader. The contact-span model reads the text in pieces of a few dozen lines; every field it finds
/// where the rules found nothing becomes a draft marked as the model's. A rule's field is never replaced, shortened or split: a model span
/// that touches a character a rule took is dropped. Where the model sees a contact block begin is kept as a line number, for grouping.
/// </summary>
internal sealed partial class TextFieldReader
{
    // A piece is a few dozen lines: enough context to tell a signature from the page around it, small enough for one pass of the encoder.
    private const int MaxPieceLines = 40;
    private const int MaxPieceChars = 3000;

    // How sure the model must be, as the mean probability of the words of the span. Chosen on development blocks, the largest cut whose scores
    // stay within 0.5 points of no cut on every field (0.4; at 0.5 the company scores lose more than two points).
    // Settable inside the assembly (and by the evaluation tools) so that a cut can be compared on development blocks; there is no public option.
    internal static float MinConfidence { get; set; } = 0.4f;

    // On a web page the model reads only inside contact blocks, and a person or organization it finds there must be surer: on web-wrapped
    // dev blocks a cut of 0.6 instead of 0.4 costs 0.2 points of person F1 and 2.2 of company F1, and real pages are noisier than those.
    // Settable inside the assembly (and by the evaluation tools); there is no public option.
    internal static float WebMinConfidence { get; set; } = 0.6f;

    // False reads web pages with the rules and the markup alone, as before.
    internal static bool ModelOnWeb { get; set; } = true;

    private void RunModel(IContactSpanModel model)
    {
        // On a web page the model's people and organizations count only inside a contact block (see InContactBlock), and nothing else it
        // finds is used: on real pages its extra addresses were mostly page text, and its names split records wrongly when it was
        // left free to read the whole page. See docs/extraction.md, "The span model on real pages".
        bool web = !_view.IsPlain;
        if (web && !ModelOnWeb)
        {
            return;
        }

        if (web)
        {
            _contactLines = ContactLines();
        }

        string text = _view.Text;
        List<TextLine> lines = _view.Lines;
        var blocks = new SortedSet<int>();
        int l = 0;
        while (l < lines.Count && _drafts.Count < MaxCandidates)
        {
            int first = l;
            int pieceStart = lines[first].Start;
            int pieceEnd = lines[first].End;
            l++;
            while (l < lines.Count && l - first < MaxPieceLines && lines[l].End - pieceStart <= MaxPieceChars)
            {
                pieceEnd = lines[l].End;
                l++;
            }

            // One line longer than a piece (hostile input) is read as far as a piece goes.
            pieceEnd = Math.Min(pieceEnd, pieceStart + MaxPieceChars);
            ContactSpanResult found = model.Find(text.Substring(pieceStart, pieceEnd - pieceStart));
            if (!web)
            {
                foreach (int offset in found.BlockStarts)
                {
                    blocks.Add(_view.LineOf(pieceStart + offset));
                }
            }

            foreach (ModelSpan s in found.Spans)
            {
                AddModelSpan(s, pieceStart, web);
            }
        }

        _blockLines = [.. blocks];
        _drafts.Sort((a, b) => a.Raw.Start.CompareTo(b.Raw.Start));
    }

    // The lines of a web page that sit in a contact block: within MaxBlockGap lines of an email address, phone number or address that the
    // rules took, in the same section (container, heading and landmark). Names and organizations anywhere else on a page are prose,
    // navigation, product names and bylines, which is where the model went wrong.
    private const int MaxBlockGap = 3;

    private bool[] ContactLines()
    {
        var lines = new bool[_view.Lines.Count];
        foreach ((FieldKind kind, _, _, int line) in _read)
        {
            if (kind is not (FieldKind.Email or FieldKind.Phone or FieldKind.Address) || line < 0 || line >= lines.Length)
            {
                continue;
            }

            lines[line] = true;
            for (int d = 1; d <= MaxBlockGap; d++)
            {
                if (line - d >= 0 && SameSection(line - d, line))
                {
                    lines[line - d] = true;
                }

                if (line + d < lines.Length && SameSection(line, line + d))
                {
                    lines[line + d] = true;
                }
            }
        }

        return lines;
    }

    private bool SameSection(int a, int b)
    {
        TextLine x = _view.Lines[a];
        TextLine y = _view.Lines[b];
        return x.Container == y.Container && x.Heading == y.Heading && x.Landmark == y.Landmark;
    }

    private bool[] _contactLines = [];

    private void AddModelSpan(ModelSpan s, int pieceStart, bool web)
    {
        FieldKind kind;
        switch (s.Label)
        {
            case "person": kind = FieldKind.Name; break;
            case "company": kind = FieldKind.Organization; break;
            case "address": kind = FieldKind.Address; break;
            case "phone": kind = FieldKind.Phone; break;
            case "email": kind = FieldKind.Email; break;
            case "website": kind = FieldKind.Website; break;
            default: return;
        }

        if (web && (kind is not (FieldKind.Name or FieldKind.Organization) || !_contactLines[Math.Min(_view.LineOf(pieceStart + s.Start), _contactLines.Length - 1)]))
        {
            return;
        }

        int start = pieceStart + s.Start;
        int end = pieceStart + s.End;
        if (s.Confidence < (web ? WebMinConfidence : MinConfidence) || end > _view.Text.Length || IsTaken(start, end))
        {
            return;
        }

        string value = _view.Text[start..end];
        // The model only finds where a value is; the core parsers decide whether it is one.
        bool ok = kind switch
        {
            FieldKind.Email => EmailAddress.TryParse(ContactText.EmailFrom(value), out _),
            FieldKind.Website => WebAddress.TryParse(value.Trim(), out WebAddress w) && w.RegistrableDomain is not null,
            _ => true,
        };
        if (!ok)
        {
            return;
        }

        Take(start, end);
        Add(kind, start, end, value, _view.LineOf(start), false, s.Confidence, kind == FieldKind.Phone, null, kind == FieldKind.Organization, model: true);
    }
}
