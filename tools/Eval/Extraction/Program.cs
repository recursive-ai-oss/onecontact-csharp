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
if (Environment.GetEnvironmentVariable("ONECONTACT_SPAN_CUT") is { Length: > 0 } cut)
{
    RecursiveAI.OneContact.Extraction.Text.TextFieldReader.MinConfidence = float.Parse(cut, CultureInfo.InvariantCulture);
}

if (Environment.GetEnvironmentVariable("ONECONTACT_WEB_MODEL") == "0")
{
    RecursiveAI.OneContact.Extraction.Text.TextFieldReader.ModelOnWeb = false;
}

if (Environment.GetEnvironmentVariable("NAME_TRACE") is { Length: > 0 })
{
    RecursiveAI.OneContact.Extraction.Text.NameCandidates.Trace = Console.WriteLine;
}

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

return mode switch { "dev" => Dev(blocks, model), "web" => Web(blocks, model), _ => Speed(blocks, model, count) };

static int Dev(List<Block> blocks, Tier1Model model)
{
    var extractor = new ContactExtractor { Sources = ExtractionSources.Text, DefaultRegion = Region.US };
    Dictionary<string, Tally> rules = Score(blocks, extractor, "rules only");
    foreach (string label in rules.Keys.Order(StringComparer.Ordinal))
    {
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{label,-9} rules only: exact P {rules[label].Precision(false):F4} R {rules[label].Recall(false):F4} F1 {rules[label].F1(false):F4}; overlap P {rules[label].Precision(true):F4} R {rules[label].Recall(true):F4}; gold {rules[label].Gold}"));
    }

    if (Environment.GetEnvironmentVariable("RULES_ONLY") is { Length: > 0 })
    {
        return 0;
    }

    ContactModels.Register(Tier1Model.PackageId, 1, model, model, model);
    Dictionary<string, Tally> both = Score(blocks, extractor, "rules + model");
    foreach (string label in rules.Keys.Order(StringComparer.Ordinal))
    {
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{label,-9} exact F1 rules {rules[label].F1(false):F4} -> +model {both[label].F1(false):F4}   overlap F1 {rules[label].F1(true):F4} -> {both[label].F1(true):F4}   gold {rules[label].Gold}"));
    }

    return 0;
}

static string Fold(string s) => new string(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

// Page-view dev blocks wrapped as HTML pages, scored by value (a page counts a value once): the web path of the extractor, with and without the model.
static int Web(List<Block> blocks, Tier1Model model)
{
    List<Block> pages = [.. blocks.Where(b => b.Id.EndsWith(".page", StringComparison.Ordinal))];
    var extractor = new ContactExtractor { Sources = ExtractionSources.Markup | ExtractionSources.Text, DefaultRegion = Region.US };
    string[] labels = ["person", "company", "address", "phone", "email", "website"];
    foreach (string title in new[] { "rules", "rules + model" })
    {
        if (title != "rules")
        {
            ContactModels.Register(Tier1Model.PackageId, 1, model, model, model);
        }

        var tp = labels.ToDictionary(l => l, _ => 0);
        var fp = labels.ToDictionary(l => l, _ => 0);
        var gold = labels.ToDictionary(l => l, _ => 0);
        int negativePagesWithName = 0;
        int negativePages = 0;
        foreach (Block b in pages)
        {
            var sb = new StringBuilder("<!doctype html><html><head><title>Page</title></head><body><main>");
            foreach (string line in b.Text.Split('\n'))
            {
                sb.Append("<p>").Append(System.Net.WebUtility.HtmlEncode(line)).Append("</p>");
            }

            sb.Append("</main></body></html>");
            ExtractResult r = extractor.ExtractHtml(sb.ToString());
            var inOrganization = new HashSet<int>(r.Records.Where(rec => rec.Kind == ContactRecordKind.Organization).SelectMany(rec => rec.Fields).Select(f => f.Id));
            var found = new HashSet<(string, string)>();
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
                if (label is not null)
                {
                    found.Add((label, Fold(f.Text)));
                }
            }

            var want = new HashSet<(string, string)>(b.Spans.Select(s => (s.Label, Fold(b.Text[s.S..s.E]))));
            foreach (string l in labels)
            {
                gold[l] += want.Count(w => w.Item1 == l);
                tp[l] += found.Count(f => f.Item1 == l && want.Contains(f));
                fp[l] += found.Count(f => f.Item1 == l && !want.Contains(f));
            }

            if (b.Spans.Count == 0)
            {
                negativePages++;
                if (found.Any(f => f.Item1 is "person" or "company"))
                {
                    negativePagesWithName++;
                }
            }
        }

        Console.WriteLine($"== web pages ({pages.Count}), {title}; negative pages with a person or company: {negativePagesWithName}/{negativePages}");
        foreach (string l in labels)
        {
            double p = tp[l] + fp[l] == 0 ? 0 : (double)tp[l] / (tp[l] + fp[l]);
            double rc = gold[l] == 0 ? 0 : (double)tp[l] / gold[l];
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{l,-9} value P {p:F4} R {rc:F4} F1 {(p + rc == 0 ? 0 : 2 * p * rc / (p + rc)):F4} gold {gold[l]} fp {fp[l]}"));
        }

        if (Environment.GetEnvironmentVariable("RULES_ONLY") is { Length: > 0 })
        {
            break;
        }
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
        if (Environment.GetEnvironmentVariable("SHOW_FP") is { Length: > 0 } fp && title == "rules only")
        {
            foreach ((string fl, int fs, int fe) in found.Where(f => f.Label == fp))
            {
                if (!b.Spans.Any(g => g.Label == fl && g.S == fs && g.E == fe) && Tally.Shown++ < 60)
                {
                    Console.WriteLine($"FP {fl} [{b.Text[fs..fe].Replace("\n", "\\n")}] in [{b.Text[Math.Max(0, fs - 30)..Math.Min(b.Text.Length, fe + 30)].Replace("\n", "\\n")}] gold-here: {string.Join(" ; ", b.Spans.Where(g => g.S < fe && fs < g.E).Select(g => g.Label + ":" + b.Text[g.S..g.E]))}");
                }
            }
        }

        if (Environment.GetEnvironmentVariable("SHOW_MISSES") is { Length: > 0 } show && title == (Environment.GetEnvironmentVariable("RULES_ONLY") is { Length: > 0 } ? "rules only" : "rules + model"))
        {
            foreach ((string gl, int gs, int ge) in b.Spans.Where(g => g.Label == show))
            {
                if (!found.Any(f => f.Label == gl && f.S == gs && f.E == ge) && Tally.Shown++ < 40)
                {
                    Console.WriteLine($"MISS {gl} [{b.Text[gs..ge].Replace("\n", "\\n")}] in [{b.Text[Math.Max(0, gs - 40)..Math.Min(b.Text.Length, ge + 50)].Replace("\n", "\\n")}] found-here: {string.Join(" ; ", found.Where(f => f.S < ge && gs < f.E).Select(f => f.Label + ":" + b.Text[f.S..f.E].Replace("\n", "\\n")))}");
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

        result.Add(new Block(root.GetProperty("text").GetString()!, spans) { Id = root.GetProperty("id").GetString()! });
        if (result.Count == count)
        {
            break;
        }
    }

    return result;
}

internal sealed record Block(string Text, List<(string Label, int S, int E)> Spans)
{
    public string Id { get; init; } = string.Empty;
}

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

    public double Precision(bool overlap) => _found == 0 ? 0 : (double)(overlap ? _foundOverlap : _exactTp) / _found;

    public double Recall(bool overlap) => Gold == 0 ? 0 : (double)(overlap ? _overlapTp : _exactTp) / Gold;

    public double F1(bool overlap)
    {
        double recall = Gold == 0 ? 0 : (double)(overlap ? _overlapTp : _exactTp) / Gold;
        double precision = _found == 0 ? 0 : (double)(overlap ? _foundOverlap : _exactTp) / _found;
        return precision + recall == 0 ? 0 : 2 * precision * recall / (precision + recall);
    }
}
