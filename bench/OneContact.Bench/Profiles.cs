using System.Diagnostics;
using System.Globalization;
using RecursiveAI.OneContact.Extraction;
using RecursiveAI.OneContact.Extraction.Markup;
using RecursiveAI.OneContact.Extraction.Obfuscation;
using RecursiveAI.OneContact.Extraction.Text;
using RecursiveAI.OneContact.Models;
using RecursiveAI.OneContact.Models.Tier1;
using RecursiveAI.OneContact.Names;
using RecursiveAI.OneHtml;
using RecursiveAI.OneHtml.Accessibility;
using RecursiveAI.OneHtml.Css;

namespace RecursiveAI.OneContact.Bench;

/// <summary>Times the phases of an extraction, one by one, and counts how often name parsing calls the model. Run as <c>profile-extract</c> or <c>profile-names</c>.</summary>
internal static class Profiles
{
    public static int Extract()
    {
        Inputs.Page[] pages = Enumerable.Range(1, 8).Select(i => Inputs.MakePage(i, 24)).ToArray();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"page: {pages[0].Html.Length} chars of HTML, {pages[0].Text.Length} of text"));
        var extractor = new ContactExtractor { Sources = ExtractionSources.Markup | ExtractionSources.Text, DefaultRegion = Region.US };
        try
        {
            Document d0 = Document.Parse(pages[0].Html);
            DocumentStyles st0 = DocumentStyles.Create(d0);
            AccessibilityTree tree0 = AccessibilityTree.Build(d0, st0);
            NavigationLists lists0 = tree0.CreateNavigationLists();
            Console.WriteLine($"cascade ok: {lists0.Landmarks.Count} landmarks, {lists0.Headings.Count} headings");
        }
        catch (Exception e)
        {
            Console.WriteLine($"cascade FAILED: {e.GetType().Name}: {e.Message}");
        }

        Time("Document.Parse", pages, p => Document.Parse(p.Html).GetHashCode());
        Document[] docs = pages.Select(p => Document.Parse(p.Html)).ToArray();
        Time("DocumentStyles.Create", pages, p => DocumentStyles.Create(Document.Parse(p.Html)).GetHashCode());
        Time("styles + AccessibilityTree.Build", pages, p =>
        {
            Document d = Document.Parse(p.Html);
            DocumentStyles st = DocumentStyles.Create(d);
            return AccessibilityTree.Build(d, st).GetHashCode();
        });
        Time("styles + tree + CreateNavigationLists", pages, p =>
        {
            Document d = Document.Parse(p.Html);
            DocumentStyles st = DocumentStyles.Create(d);
            return AccessibilityTree.Build(d, st).CreateNavigationLists().Landmarks.Count;
        });
        Time("parse + VisibleTextReader.Read", pages, p =>
        {
            var ctx = new PageContext(Document.Parse(p.Html), p.Html);
            return VisibleTextReader.Read(ctx, out _).GetHashCode();
        });
        Time("plain text: PlainTextView + TextFieldReader", pages, p =>
        {
            PageContext ctx = PageContext.ForPlainText(p.Text);
            TextView view = PlainTextView.Build(p.Text);
            return TextFieldReader.Read(ctx, view, new MatcherPhoneFinder(Region.US), null, out _).Count;
        });
        Time("  EmailFinder.Find per line", pages, p => PerLine(p, (view, text, l, sink) => EmailFinder.Find(text, l.Start, l.End, sink.Emails)));
        Time("  LabelCandidates.TryFind per line", pages, p => PerLine(p, (view, text, l, sink) => LabelCandidates.TryFind(text, l.Start, l.End, out _)));
        Time("  AddressCandidates.Find (view)", pages, p =>
        {
            var hits = new List<AddressHit>();
            AddressCandidates.Find(PlainTextView.Build(p.Text), hits);
            return hits.Count;
        });
        Time("  UrlCandidates.Find per line", pages, p => PerLine(p, (view, text, l, sink) => UrlCandidates.Find(text, l.Start, l.End, sink.Urls)));
        Time("  ShapePhoneFinder per line", pages, p => PerLine(p, (view, text, l, sink) => ShapePhoneFinder.Instance.Find(text, l.Start, l.End, sink.Phones)));
        var matcherFinder = new MatcherPhoneFinder(Region.US);
        Time("  MatcherPhoneFinder per line", pages, p => PerLine(p, (view, text, l, sink) => matcherFinder.Find(text, l.Start, l.End, sink.Phones)));
        Time("  PlainTextView.Build only", pages, p => PlainTextView.Build(p.Text).Lines.Count);
        Time("ExtractText (all)", pages, p => extractor.ExtractText(p.Text).Fields.Count);
        Time("ExtractHtml (all)", pages, p => extractor.ExtractHtml(p.Html).Fields.Count);
        return 0;
    }

    public static int Names()
    {
        using FileStream stream = File.OpenRead(Environment.GetEnvironmentVariable("ONECONTACT_TIER1_MODEL") ?? throw new InvalidOperationException("set ONECONTACT_TIER1_MODEL"));
        Tier1Model model = Tier1Model.Load(stream);
        var counting = new CountingModel(model);
        using IDisposable scope = ContactModels.Scope(model, counting, model);
        List<string> names = [.. Inputs.Names];
        string? dev = Environment.GetEnvironmentVariable("ONECONTACT_NAMES_DEV");
        if (dev is not null)
        {
            names.AddRange(File.ReadLines(dev).Take(2000).Select(Texts.FirstText));
        }

        foreach (string n in names.Take(5))
        {
            NameParsing.Split(n);
        }

        counting.Calls = 0;
        var sw = Stopwatch.StartNew();
        foreach (string n in names)
        {
            _ = PersonName.TryParse(n, out _);
        }

        sw.Stop();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"PersonName.TryParse: {names.Count} names, {counting.Calls} model calls ({100.0 * counting.Calls / names.Count:F1}%), {sw.Elapsed.TotalMilliseconds * 1000 / names.Count:F1} us per name"));
        counting.Calls = 0;
        sw.Restart();
        foreach (string n in names)
        {
            NameParsing.Split(n);
        }

        sw.Stop();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"NameParsing.Split: {counting.Calls} model calls ({100.0 * counting.Calls / names.Count:F1}%), {sw.Elapsed.TotalMilliseconds * 1000 / names.Count:F1} us per name"));
        sw.Restart();
        int k = 0;
        foreach (string n in names.Take(2000))
        {
            model.Analyze(n);
            k++;
        }

        sw.Stop();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"model.Analyze alone: {sw.Elapsed.TotalMilliseconds * 1000 / k:F1} us per call"));
        return 0;
    }

    private sealed class Sinks
    {
        public readonly List<EmailHit> Emails = [];
        public readonly List<UrlHit> Urls = [];
        public readonly List<PhoneHit> Phones = [];
    }

    private static int PerLine(Inputs.Page p, Action<TextView, string, TextLine, Sinks> work)
    {
        TextView view = PlainTextView.Build(p.Text);
        string text = view.Text;
        var sink = new Sinks();
        int n = 0;
        foreach (TextLine l in view.Lines)
        {
            sink.Emails.Clear();
            sink.Urls.Clear();
            sink.Phones.Clear();
            work(view, text, l, sink);
            n += sink.Emails.Count + sink.Urls.Count + sink.Phones.Count;
        }

        return n;
    }

    private static void Time(string label, Inputs.Page[] pages, Func<Inputs.Page, int> work)
    {
        foreach (Inputs.Page p in pages)
        {
            work(p);
        }

        double best = double.MaxValue;
        long bytes = 0;
        for (int round = 0; round < 7; round++)
        {
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            for (int rep = 0; rep < 5; rep++)
            {
                foreach (Inputs.Page p in pages)
                {
                    work(p);
                }
            }

            sw.Stop();
            bytes = GC.GetAllocatedBytesForCurrentThread() - a0;
            best = Math.Min(best, sw.Elapsed.TotalMilliseconds / (5 * pages.Length));
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{label,-46} {best,8:F3} ms/page  {bytes / (5 * pages.Length) / 1024,6} KB/page"));
    }

    private sealed class CountingModel : INameModel
    {
        private readonly INameModel _inner;

        public CountingModel(INameModel inner) => _inner = inner;

        public int Calls { get; set; }

        public NameModelResult Analyze(string text)
        {
            Calls++;
            return _inner.Analyze(text);
        }
    }

    private static class Texts
    {
        // A line of a names dev set is JSON with a "text" member.
        public static string FirstText(string json)
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        }
    }
}
