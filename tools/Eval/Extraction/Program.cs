using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Recognizers.Text;
using Microsoft.Recognizers.Text.Sequence;
using RecursiveAI.OneContact;
using RecursiveAI.OneContact.Extraction;
using RecursiveAI.OneContact.Extraction.Records;
using ExtractResult = RecursiveAI.OneContact.Extraction.ExtractResult;
using RecursiveAI.OneContact.Models;
using RecursiveAI.OneContact.Models.Tier1;

// usage:
//   eval-extraction dev   <model.onenlp> <blocks dev jsonl> [count]   field scores of text extraction, rules only and rules + model
//   eval-extraction speed <model.onenlp> <blocks dev jsonl> [pages]   indicative pages/s on synthetic pages, next to Microsoft.Recognizers.Text
// The input is the rendered contact-block dev set (never evaluation text). A field is right when its kind and its start and end offsets equal the
// label's ("exact"), or when it overlaps a label of the same kind ("overlap").
string mode = args[0];
string modelPath = args[1];
string blocksPath = args[2];
int count = args.Length > 3 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 5000;

List<Block> blocks = ReadBlocks(blocksPath, mode == "speed" ? count * 20 : count);
Tier1Model model;
var load = Stopwatch.StartNew();
using (FileStream stream = File.OpenRead(modelPath))
{
    model = Tier1Model.Load(stream);
}

model.Find("Jane Smith\njane@example.com");
load.Stop();
Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"model load + first call: {load.Elapsed.TotalMilliseconds:F0} ms; blocks: {blocks.Count}"));

return mode == "dev" ? Dev(blocks, model) : Speed(blocks, model, count);

static int Dev(List<Block> blocks, Tier1Model model)
{
    var extractor = new ContactExtractor { Sources = ExtractionSources.Text, DefaultRegion = Region.US };
    Dictionary<string, Tally> rules = Score(blocks, extractor, "rules only");
    ContactModels.Register(Tier1Model.PackageId, 1, model, model, model);
    Dictionary<string, Tally> both = Score(blocks, extractor, "rules + model");
    foreach (string label in rules.Keys.Order(StringComparer.Ordinal))
    {
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{label,-9} exact F1 rules {rules[label].F1(false):F4} -> +model {both[label].F1(false):F4}   overlap F1 {rules[label].F1(true):F4} -> {both[label].F1(true):F4}   gold {rules[label].Gold}"));
    }

    return 0;
}

static Dictionary<string, Tally> Score(List<Block> blocks, ContactExtractor extractor, string title)
{
    var tallies = new Dictionary<string, Tally>(StringComparer.Ordinal);
    foreach (string l in new[] { "person", "company", "address", "phone", "email", "website" })
    {
        tallies[l] = new Tally();
    }

    foreach (Block b in blocks)
    {
        ExtractResult r = extractor.ExtractText(b.Text);
        var found = new List<(string Label, int S, int E)>();
        // A name in an organization record is the organization's name.
        var inOrganization = new HashSet<int>(r.Records.Where(rec => rec.Kind == ContactRecordKind.Organization).SelectMany(rec => rec.Fields).Select(f => f.Id));
        foreach (ExtractedField f in r.Fields)
        {
            string? label = f.Kind switch
            {
                FieldKind.Name => inOrganization.Contains(f.Id) ? "company" : "person",
                FieldKind.Organization => "company",
                FieldKind.Address => "address",
                FieldKind.Phone => "phone",
                FieldKind.Email => "email",
                FieldKind.Website => "website",
                _ => null,
            };
            if (label is not null && f.HasSpan)
            {
                found.Add((label, f.Span.Start, f.Span.Start + f.Span.Length));
            }
        }

        // A name the rules read after a "Company:" label, or a person the grouping took for an organization, is still the field it is.
        if (Environment.GetEnvironmentVariable("SHOW_MISSES") is { Length: > 0 } show && title == "rules + model")
        {
            foreach ((string gl, int gs, int ge) in b.Spans.Where(g => g.Label == show))
            {
                if (!found.Any(f => f.Label == gl && f.S == gs && f.E == ge) && Tally.Shown++ < 40)
                {
                    Console.WriteLine($"MISS {gl} [{b.Text[gs..ge].Replace("\n", "\\n")}] found-here: {string.Join(" ; ", found.Where(f => f.S < ge && gs < f.E).Select(f => f.Label + ":" + b.Text[f.S..f.E].Replace("\n", "\\n")))}");
                }
            }
        }

        foreach (string label in tallies.Keys)
        {
            tallies[label].Add(b.Spans.Where(s => s.Label == label).ToList(), found.Where(f => f.Label == label).Select(f => (f.S, f.E)).ToList());
        }
    }

    Console.WriteLine(title + " done");
    return tallies;
}

static int Speed(List<Block> blocks, Tier1Model model, int pages)
{
    var html = new List<string>();
    var plain = new List<string>();
    for (int i = 0; i + 20 <= blocks.Count && html.Count < pages; i += 20)
    {
        var sb = new StringBuilder("<!doctype html><html><head><title>Contact</title></head><body><main>");
        var text = new StringBuilder();
        for (int k = i; k < i + 20; k++)
        {
            sb.Append("<section>");
            foreach (string line in blocks[k].Text.Split('\n'))
            {
                sb.Append("<p>").Append(System.Net.WebUtility.HtmlEncode(line)).Append("</p>");
                text.Append(line).Append('\n');
            }

            sb.Append("</section>");
        }

        sb.Append("</main></body></html>");
        html.Add(sb.ToString());
        plain.Add(text.ToString());
    }

    long chars = plain.Sum(p => p.Length);
    var extractor = new ContactExtractor { Sources = ExtractionSources.Markup | ExtractionSources.Text, DefaultRegion = Region.US };
    Time("extraction, rules only", html, chars, h => extractor.ExtractHtml(h));
    ContactModels.Register(Tier1Model.PackageId, 1, model, model, model);
    Time("extraction, rules + model", html, chars, h => extractor.ExtractHtml(h));
    Time("Microsoft.Recognizers.Text (email, phone, URL)", plain, chars, t =>
    {
        SequenceRecognizer.RecognizeEmail(t, Culture.English);
        SequenceRecognizer.RecognizePhoneNumber(t, Culture.English);
        SequenceRecognizer.RecognizeURL(t, Culture.English);
    });
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"peak working set: {Process.GetCurrentProcess().PeakWorkingSet64 / 1048576.0:F0} MB"));
    return 0;
}

static void Time(string label, List<string> items, long chars, Action<string> work)
{
    foreach (string t in items.Take(5))
    {
        work(t);
    }

    long bytes0 = GC.GetAllocatedBytesForCurrentThread();
    var sw = Stopwatch.StartNew();
    foreach (string t in items)
    {
        work(t);
    }

    sw.Stop();
    long bytes = GC.GetAllocatedBytesForCurrentThread() - bytes0;
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{label,-50} {items.Count / sw.Elapsed.TotalSeconds,9:F1} pages/s  {chars / 1048576.0 / sw.Elapsed.TotalSeconds,7:F2} MB/s of text  {bytes / items.Count / 1024,8:F0} KB/page"));
}

static List<Block> ReadBlocks(string path, int count)
{
    List<Block> result = [];
    foreach (string line in File.ReadLines(path))
    {
        using JsonDocument doc = JsonDocument.Parse(line);
        JsonElement root = doc.RootElement;
        var spans = new List<(string Label, int S, int E)>();
        foreach (JsonElement s in root.GetProperty("spans").EnumerateArray())
        {
            spans.Add((s[2].GetString()!, s[0].GetInt32(), s[1].GetInt32()));
        }

        result.Add(new Block(root.GetProperty("text").GetString()!, spans));
        if (result.Count == count)
        {
            break;
        }
    }

    return result;
}

internal sealed record Block(string Text, List<(string Label, int S, int E)> Spans);

internal sealed class Tally
{
    public static int Shown;

    private long _exactTp, _overlapTp, _found, _foundOverlap;

    public long Gold { get; private set; }

    public void Add(List<(string Label, int S, int E)> gold, List<(int S, int E)> found)
    {
        Gold += gold.Count;
        _found += found.Count;
        foreach ((string _, int s, int e) in gold)
        {
            if (found.Contains((s, e)))
            {
                _exactTp++;
            }

            if (found.Any(f => f.S < e && s < f.E))
            {
                _overlapTp++;
            }
        }

        foreach ((int s, int e) in found)
        {
            if (gold.Any(g => g.S < e && s < g.E))
            {
                _foundOverlap++;
            }
        }
    }

    public double F1(bool overlap)
    {
        double recall = Gold == 0 ? 0 : (double)(overlap ? _overlapTp : _exactTp) / Gold;
        double precision = _found == 0 ? 0 : (double)(overlap ? _foundOverlap : _exactTp) / _found;
        return precision + recall == 0 ? 0 : 2 * precision * recall / (precision + recall);
    }
}
