using RecursiveAI.OneContact.Extraction.Markup;
using RecursiveAI.OneContact.Extraction.Obfuscation;
using RecursiveAI.OneContact.Extraction.Records;
using RecursiveAI.OneContact.Models;
using RecursiveAI.OneHtml;
using RecursiveAI.OneHtml.Parsing;

namespace RecursiveAI.OneContact.Extraction.Text;

/// <summary>
/// The rules that read contact fields out of visible text: email addresses (obfuscated forms decoded), web addresses, phone numbers,
/// United States and Canadian addresses, and names that a label states. Each candidate becomes a <see cref="FieldDraft"/> of source
/// <see cref="FieldSource.Text"/> whose raw range is the text as written in the original input. Nothing here chooses a region: the
/// core parsers are given one later, by the same rule the markup fields follow.
/// </summary>
internal sealed partial class TextFieldReader
{
    private const int MaxCandidates = 50_000;

    private readonly PageContext _page;
    private readonly TextView _view;
    private readonly IPhoneCandidateFinder _phones;
    private readonly bool[] _taken;
    private readonly List<FieldDraft> _drafts = [];
    private readonly List<(FieldKind Kind, int Start, int End, int Line)> _read = [];

    private int[] _blockLines = [];

    private TextFieldReader(PageContext page, TextView view, IPhoneCandidateFinder phones)
    {
        _page = page;
        _view = view;
        _phones = phones;
        _taken = new bool[view.Text.Length];
    }

    /// <summary>Reads the fields of a view of the text.</summary>
    /// <param name="page">The page (for source slices and elements).</param>
    /// <param name="view">The visible text.</param>
    /// <param name="phones">The phone candidate finder; null for the shape rules.</param>
    /// <returns>The fields, in order of position.</returns>
    public static List<FieldDraft> Read(PageContext page, TextView view, IPhoneCandidateFinder? phones = null)
        => Read(page, view, phones, null, out _);

    /// <summary>Reads the fields of a view of the text, then lets a contact-span model add what the rules found nothing for.</summary>
    /// <param name="page">The page (for source slices and elements).</param>
    /// <param name="view">The visible text.</param>
    /// <param name="phones">The phone candidate finder; null for the shape rules.</param>
    /// <param name="model">The model, or null for rules only.</param>
    /// <param name="blockLines">The lines where the model sees a contact block begin, increasing; empty without a model.</param>
    /// <returns>The fields, in order of position.</returns>
    public static List<FieldDraft> Read(PageContext page, TextView view, IPhoneCandidateFinder? phones, IContactSpanModel? model, out int[] blockLines)
    {
        var reader = new TextFieldReader(page, view, phones ?? ShapePhoneFinder.Instance);
        reader.Run();
        if (model is not null)
        {
            reader.RunModel(model);
        }

        blockLines = reader._blockLines;
        if (!view.IsPlain)
        {
            reader.ReadCloudflare();
        }

        return reader._drafts;
    }

    private void Run()
    {
        string text = _view.Text;
        var emails = new List<EmailHit>();
        for (int l = 0; l < _view.Lines.Count && _drafts.Count < MaxCandidates; l++)
        {
            TextLine line = _view.Lines[l];
            emails.Clear();
            try
            {
                EmailFinder.Find(text, line.Start, line.End, emails);
            }
            catch (ArgumentException)
            {
                // A page and a string are hostile input: a line that trips a defect in a core parser is skipped, not the whole page.
                emails.Clear();
            }

            foreach (EmailHit h in emails)
            {
                Take(h.Start, h.End);
                Add(FieldKind.Email, h.Start, h.End, h.Address, l, h.Decoded, h.Decoded ? 0.8 : 0.9, false, null, false);
            }

            if (LabelCandidates.TryFind(text, line.Start, line.End, out LabelHit label))
            {
                Take(label.ValueStart, label.ValueEnd);
                Add(label.Company ? FieldKind.Organization : FieldKind.Name, label.ValueStart, label.ValueEnd, text[label.ValueStart..label.ValueEnd], l, false, 0.8, false, null, label.Company);
            }
        }

        var addresses = new List<AddressHit>();
        AddressCandidates.Find(_view, addresses);
        foreach (AddressHit a in addresses)
        {
            if (IsTaken(a.Start, a.End))
            {
                continue;
            }

            Take(a.Start, a.End);
            Add(FieldKind.Address, a.Start, a.End, a.Text, a.LastLine, false, 0.7, false, null, false);
        }

        var urls = new List<UrlHit>();
        var phoneHits = new List<PhoneHit>();
        for (int l = 0; l < _view.Lines.Count && _drafts.Count < MaxCandidates; l++)
        {
            TextLine line = _view.Lines[l];
            urls.Clear();
            UrlCandidates.Find(text, line.Start, line.End, urls);
            foreach (UrlHit u in urls)
            {
                if (IsTaken(u.Start, u.End))
                {
                    continue;
                }

                Take(u.Start, u.End);
                string t = text[u.Start..u.End];
                double confidence = t.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? 0.85 : t.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? 0.8 : 0.6;
                Add(FieldKind.Website, u.Start, u.End, t, l, false, confidence, false, null, false);
            }

            phoneHits.Clear();
            _phones.Find(text, line.Start, line.End, phoneHits);
            foreach (PhoneHit p in phoneHits)
            {
                if (IsTaken(p.Start, p.End))
                {
                    continue;
                }

                Take(p.Start, p.End);
                string t = text[p.Start..p.End];
                double confidence = p.Labelled ? 0.85 : t.StartsWith('+') ? 0.8 : 0.65;
                Add(FieldKind.Phone, p.Start, p.End, t, l, false, confidence, !p.Labelled, p.Label, false);
            }
        }

        ReadNames();
        _drafts.Sort((a, b) => a.Raw.Start.CompareTo(b.Raw.Start));
    }

    // People and organizations the rules can tell from what stands next to them (see NameCandidates).
    private void ReadNames()
    {
        foreach (NameHit h in NameCandidates.Find(_view, _read, IsTaken, SideBySide))
        {
            if (_drafts.Count >= MaxCandidates)
            {
                break;
            }

            Take(h.Start, h.End);
            Add(h.Organization ? FieldKind.Organization : FieldKind.Name, h.Start, h.End, _view.Text[h.Start..h.End], h.Line, false, h.Organization ? 0.75 : 0.7, false, null, h.Organization, nameRule: true);
        }
    }

    // Text at two indexes comes from elements that are neither the same nor one inside the other.
    private bool SideBySide(int a, int b)
    {
        if (_view.IsPlain)
        {
            return false;
        }

        int x = _view.ElementOf(a);
        int y = _view.ElementOf(b);
        return x >= 0 && y >= 0 && x != y && !Encloses(x, y) && !Encloses(y, x);
    }

    private bool Encloses(int outer, int inner)
    {
        for (Node n = _page.GetNode(inner); !n.IsNull; n = n.Parent)
        {
            if (n.Index == outer)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsTaken(int start, int end)
    {
        for (int i = start; i < end && i < _taken.Length; i++)
        {
            if (_taken[i])
            {
                return true;
            }
        }

        return false;
    }

    private void Take(int start, int end)
    {
        for (int i = start; i < end && i < _taken.Length; i++)
        {
            _taken[i] = true;
        }
    }

    private void Add(FieldKind kind, int start, int end, string value, int line, bool decoded, double confidence, bool requireValid, string? label, bool companyLabel, bool model = false, bool nameRule = false)
    {
        if (!model && !nameRule)
        {
            _read.Add((kind, start, end, line));
        }

        TextRange raw = _view.RangeOf(start, end, out bool gap, out bool reversed);
        bool obfuscated = decoded || reversed || (kind is FieldKind.Email or FieldKind.Website && gap);
        if (!obfuscated && !_view.IsPlain && raw.IsPresent && kind is FieldKind.Email or FieldKind.Website)
        {
            obfuscated = HasObfuscatingReference(_page.Slice(raw));
        }

        int element = _view.IsPlain ? -1 : _view.ElementOf(start);
        _drafts.Add(new FieldDraft
        {
            Kind = kind,
            Source = FieldSource.Text,
            Text = value,
            Raw = raw,
            Label = label,
            ElementIndex = element,
            BlockId = NearestAddress(element),
            Deobfuscated = obfuscated,
            Confidence = confidence,
            RequireValid = requireValid,
            Line = line,
            CompanyLabel = companyLabel,
            Model = model,
        });
    }

    private int NearestAddress(int element)
    {
        if (element < 0)
        {
            return -1;
        }

        for (Node n = _page.GetNode(element); !n.IsNull; n = n.Parent)
        {
            if (n.IsElement && n.LocalName == "address")
            {
                return n.Index;
            }
        }

        return -1;
    }

    // An address written with character references for letters, digits and the marks of an address: "jo&#104;n&#64;example&#46;com". A
    // reference for "&" or "<" is ordinary markup and says nothing.
    private static bool HasObfuscatingReference(string raw)
    {
        int at = raw.IndexOf('&', StringComparison.Ordinal);
        while (at >= 0)
        {
            int end = VisibleTextReader.EntityEnd(raw, at);
            if (end > at)
            {
                string decoded = System.Net.WebUtility.HtmlDecode(raw[at..end]);
                if (decoded.Length == 1 && (char.IsAsciiLetterOrDigit(decoded[0]) || decoded[0] is '@' or '.' or '_' or '-' or '+'))
                {
                    return true;
                }

                if (raw.AsSpan(at, end - at).Equals("&commat;", StringComparison.Ordinal) || raw.AsSpan(at, end - at).Equals("&period;", StringComparison.Ordinal)
                    || raw.AsSpan(at, end - at).Equals("&lowbar;", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            at = raw.IndexOf('&', at + 1);
        }

        return false;
    }

    // Cloudflare's email protection: the address is in data-cfemail (or after the # of a /cdn-cgi/l/email-protection link) as hex.
    private void ReadCloudflare()
    {
        foreach (Node el in _page.Elements)
        {
            if (el.Attributes.Count == 0 || _drafts.Count >= MaxCandidates)
            {
                continue;
            }

            foreach (Attr a in el.Attributes)
            {
                string hex;
                TextRange range;
                if (a.LocalName == CloudflareEmail.Attribute)
                {
                    hex = a.Value.ToString();
                    range = ValueRange(a, 0);
                }
                else if (a.LocalName == "href" && a.Value.ToString() is string href && href.IndexOf(CloudflareEmail.LinkMarker, StringComparison.Ordinal) is int marker and >= 0)
                {
                    int at = marker + CloudflareEmail.LinkMarker.Length;
                    hex = href[at..];
                    range = ValueRange(a, at);
                }
                else
                {
                    continue;
                }

                if (!CloudflareEmail.TryDecode(hex, out string decoded) || _page.IsHidden(el))
                {
                    continue;
                }

                string address = ContactText.EmailFrom(decoded);
                int line = _view.LineAtSource(range.Start, _page.OuterRange(el));
                if (line < 0)
                {
                    // The element shows no text (an image link, say) or none that is visible: the address is not on the page for a reader.
                    continue;
                }

                _drafts.Add(new FieldDraft
                {
                    Kind = FieldKind.Email,
                    Source = FieldSource.Text,
                    Text = address,
                    Raw = range,
                    ElementIndex = el.Index,
                    BlockId = NearestAddress(el.Index),
                    Deobfuscated = true,
                    Confidence = 0.9,
                    Line = line,
                });
            }
        }

        _drafts.Sort((a, b) => a.Raw.Start.CompareTo(b.Raw.Start));
    }

    // The range of an attribute's value from `skip` characters in; none when the value has no source position.
    private static TextRange ValueRange(Attr a, int skip)
    {
        SourceRange r = a.ValueRange;
        if (a.Quote == AttributeQuote.NoValue || !r.IsPresent)
        {
            return TextRange.None;
        }

        return new TextRange(Math.Min(r.Start + skip, r.End), r.End);
    }
}
