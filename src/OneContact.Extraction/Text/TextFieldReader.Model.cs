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

    // How sure the model must be, as the mean probability of the words of the span. Chosen on development blocks: the mean is low for
    // correct spans (it is trained with dropout and spans have uncertain edges), and from 0.3 up to 0.45 the scores are within 0.2 points of
    // no cut at all, while 0.6 loses a fifth of company names and 0.75 loses most addresses.
    // Settable inside the assembly (and by the evaluation tools) so that a cut can be compared on development blocks; there is no public option.
    internal static float MinConfidence { get; set; } = 0.3f;

    private void RunModel(IContactSpanModel model)
    {
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
            foreach (int offset in found.BlockStarts)
            {
                blocks.Add(_view.LineOf(pieceStart + offset));
            }

            foreach (ModelSpan s in found.Spans)
            {
                AddModelSpan(s, pieceStart);
            }
        }

        _blockLines = [.. blocks];
        _drafts.Sort((a, b) => a.Raw.Start.CompareTo(b.Raw.Start));
    }

    private void AddModelSpan(ModelSpan s, int pieceStart)
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

        int start = pieceStart + s.Start;
        int end = pieceStart + s.End;
        if (s.Confidence < MinConfidence || end > _view.Text.Length || IsTaken(start, end))
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
