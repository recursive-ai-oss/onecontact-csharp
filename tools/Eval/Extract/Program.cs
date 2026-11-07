using System.Text;
using System.Text.Json;
using Microsoft.Recognizers.Text;
using Microsoft.Recognizers.Text.Sequence;
using RecursiveAI.OneContact;
using RecursiveAI.OneContact.Extraction;
using RecursiveAI.OneContact.Extraction.Markup;
using RecursiveAI.OneContact.Extraction.Records;
using RecursiveAI.OneContact.Extraction.Text;
using RecursiveAI.OneHtml;
using OneContact.Eval.Extract;
using static OneContact.Eval.Extract.Io;
using ExtractResult = RecursiveAI.OneContact.Extraction.ExtractResult;

// usage:
//   eval-extract ours-web <set-dir> <out.jsonl> [--ids ids.txt]   our extractor (markup and text) over stored pages
//   eval-extract ours-text <items.jsonl> <out.jsonl>               our extractor over plain-text items
//   eval-extract visible <set-dir> <out.jsonl> [--ids ids.txt]     each page's visible text, for the reference systems
//   eval-extract map <set-dir> <chars.jsonl> <out.jsonl>           turns character spans in visible text into byte spans of the page
//   eval-extract recognizers <in.jsonl> <out.jsonl>                Microsoft.Recognizers.Text phone, email and URL finders over {id,text}
//   eval-extract review <set-dir|items.jsonl> <pre.jsonl> <out.txt>   compact reviewer view: the pre-label beside the text it came from
//   eval-extract gold <set-dir|items.jsonl> <pre.jsonl> <corrections.jsonl> <gold.jsonl>   applies the reviewer corrections to the pre-label
//
// Prediction lines: {"id", "fields": [{"type", "start", "end", "text"}], "records": [[field index, ...]]}. Offsets are bytes of the
// stored page for pages and UTF-16 code units for text; types are person, org, address, phone, email, website. Nothing but counts is
// printed: the sets hold personal data.
if (args.Length < 3)
{
    Console.Error.WriteLine("usage: eval-extract ours-web|ours-text|visible|map|recognizers <input> <output> [...]");
    return 64;
}

// A trained model is used when ONECONTACT_TIER1_MODEL names a saved pipeline; ONECONTACT_SPAN_CUT sets the confidence cut of its text spans.
if (Environment.GetEnvironmentVariable("ONECONTACT_SPAN_CUT") is { Length: > 0 } cut)
{
    TextFieldReader.MinConfidence = float.Parse(cut, System.Globalization.CultureInfo.InvariantCulture);
}

// ONECONTACT_WEB_MODEL=1 lets the gated model read web pages (off by default: rules and markup only there).
if (Environment.GetEnvironmentVariable("ONECONTACT_WEB_MODEL") == "1")
{
    TextFieldReader.ModelOnWeb = true;
}

if (Environment.GetEnvironmentVariable("ONECONTACT_TIER1_MODEL") is { Length: > 0 } modelPath)
{
    using FileStream modelStream = File.OpenRead(modelPath);
    var tier1 = RecursiveAI.OneContact.Models.Tier1.Tier1Model.Load(modelStream);
    RecursiveAI.OneContact.Models.ContactModels.Register(RecursiveAI.OneContact.Models.Tier1.Tier1Model.PackageId, 1, tier1, tier1, tier1);
}

HashSet<string>? ids = null;
int idsAt = Array.IndexOf(args, "--ids");
if (idsAt >= 0 && idsAt + 1 < args.Length)
{
    ids = [.. File.ReadAllLines(args[idsAt + 1]).Where(l => l.Length > 0)];
}

switch (args[0])
{
    case "ours-web":
        return OursWeb(args[1], args[2], ids);
    case "ours-text":
        return OursText(args[1], args[2]);
    case "visible":
        return Visible(args[1], args[2], ids);
    case "map":
        return MapSpans(args[1], args[2], args[3]);
    case "recognizers":
        return Recognizers(args[1], args[2]);
    case "review":
        return Adjudication.Review(args[1], args[2], args[3]);
    case "lines":
        return Adjudication.ShowLines(args[1], args[2], int.Parse(args[3]), int.Parse(args[4]));
    case "gold":
        return Adjudication.BuildGold(args[1], args[2], args[3], args[4]);
    default:
        Console.Error.WriteLine("unknown command " + args[0]);
        return 64;
}

static string TypeOf(ExtractedField f, ContactRecord? record)
{
    switch (f.Kind)
    {
        case FieldKind.Phone: return "phone";
        case FieldKind.Email: return "email";
        case FieldKind.Website: return "website";
        case FieldKind.Address: return "address";
        case FieldKind.Organization: return "org";
    }

    // A name field: the record's kind decides when it is known, otherwise what the normalizer made of it.
    if (record is not null && record.Kind == ContactRecordKind.Organization)
    {
        return "org";
    }

    if (record is not null && record.Kind == ContactRecordKind.Person)
    {
        return "person";
    }

    return f.Normalized.Kind == ContactKind.Company ? "org" : "person";
}

static void WriteResult(Utf8JsonWriter w, string id, ExtractResult result)
{
    // Every mention is a field: the duplicates folded into a primary field are written too, in the same record.
    var rows = new List<(string Type, int Start, int End, string Text, int RecordId, string Source)>();
    var recordOf = new Dictionary<int, ContactRecord>();
    foreach (ContactRecord r in result.Records)
    {
        foreach (ExtractedField f in r.Fields)
        {
            recordOf[f.Id] = r;
        }
    }

    foreach (ExtractedField f in result.Fields)
    {
        recordOf.TryGetValue(f.Id, out ContactRecord? rec);
        foreach (ExtractedField m in new[] { f }.Concat(f.Duplicates))
        {
            int start = m.HasSpan ? m.Span.Start : -1;
            int end = m.HasSpan ? m.Span.Start + m.Span.Length : -1;
            rows.Add((TypeOf(f, rec), start, end, m.Text, rec?.Id ?? -1, m.Source.ToString()));
        }
    }

    w.WriteStartObject();
    w.WriteString("id", id);
    w.WriteStartArray("fields");
    foreach (var row in rows)
    {
        w.WriteStartObject();
        w.WriteString("type", row.Type);
        w.WriteNumber("start", row.Start);
        w.WriteNumber("end", row.End);
        w.WriteString("text", row.Text);
        w.WriteString("src", row.Source);
        w.WriteEndObject();
    }

    w.WriteEndArray();
    w.WriteStartArray("records");
    foreach (var group in rows.Select((r, i) => (r.RecordId, i)).Where(x => x.RecordId >= 0).GroupBy(x => x.RecordId))
    {
        w.WriteStartArray();
        foreach (var x in group)
        {
            w.WriteNumberValue(x.i);
        }

        w.WriteEndArray();
    }

    w.WriteEndArray();
    w.WriteEndObject();
}

static int OursWeb(string dir, string output, HashSet<string>? ids)
{
    int n = 0;
    int failed = 0;
    using StreamWriter o = new(output, false, new UTF8Encoding(false));
    foreach (JsonElement page in Pages(dir))
    {
        string id = page.GetProperty("id").GetString()!;
        if (ids is not null && !ids.Contains(id))
        {
            continue;
        }

        string tld = page.GetProperty("tld").GetString()!;
        string country = page.TryGetProperty("country", out JsonElement c) ? c.GetString()! : (tld == "ca" ? "CA" : "US");
        byte[] bytes = File.ReadAllBytes(Path.Combine(dir, "pages", id + ".html"));
        try
        {
            var extractor = new ContactExtractor { DefaultRegion = Region.Parse(country) };
            ExtractResult r = extractor.ExtractHtml(bytes, page.GetProperty("ctype").GetString());
            o.WriteLine(Json(w => WriteResult(w, id, r)));
            n++;
        }
        catch (Exception e) when (e is HtmlLimitExceededException or ArgumentException or FormatException)
        {
            o.WriteLine(Json(w => { w.WriteStartObject(); w.WriteString("id", id); w.WriteStartArray("fields"); w.WriteEndArray(); w.WriteStartArray("records"); w.WriteEndArray(); w.WriteEndObject(); }));
            failed++;
        }
    }

    Console.WriteLine($"pages {n} failed {failed}");
    return 0;
}

static int OursText(string input, string output)
{
    int n = 0;
    using StreamWriter o = new(output, false, new UTF8Encoding(false));
    foreach (JsonElement item in Lines(input))
    {
        string id = item.GetProperty("id").GetString()!;
        string country = item.TryGetProperty("country", out JsonElement c) ? c.GetString()! : "US";
        var extractor = new ContactExtractor { DefaultRegion = Region.Parse(country) };
        ExtractResult r = extractor.ExtractText(item.GetProperty("text").GetString()!);
        o.WriteLine(Json(w => WriteResult(w, id, r)));
        n++;
    }

    Console.WriteLine($"items {n}");
    return 0;
}

static int Visible(string dir, string output, HashSet<string>? ids)
{
    int n = 0;
    using StreamWriter o = new(output, false, new UTF8Encoding(false));
    foreach (JsonElement page in Pages(dir))
    {
        string id = page.GetProperty("id").GetString()!;
        if (ids is not null && !ids.Contains(id))
        {
            continue;
        }

        try
        {
            var (_, view) = ViewOf(dir, page);
            o.WriteLine(Json(w => { w.WriteStartObject(); w.WriteString("id", id); w.WriteString("country", page.TryGetProperty("country", out JsonElement c) ? c.GetString() : (page.GetProperty("tld").GetString() == "ca" ? "CA" : "US")); w.WriteString("text", view.Text); w.WriteEndObject(); }));
            n++;
        }
        catch (Exception e) when (e is HtmlLimitExceededException or ArgumentException)
        {
            Console.Error.WriteLine("skipped a page: " + e.GetType().Name);
        }
    }

    Console.WriteLine($"pages {n}");
    return 0;
}

static int MapSpans(string dir, string charsPath, string output)
{
    var pages = Pages(dir).ToDictionary(p => p.GetProperty("id").GetString()!);
    int n = 0;
    int unmapped = 0;
    using StreamWriter o = new(output, false, new UTF8Encoding(false));
    foreach (JsonElement pred in Lines(charsPath))
    {
        string id = pred.GetProperty("id").GetString()!;
        var (ctx, view) = ViewOf(dir, pages[id]);
        var fields = new List<(string Type, int Start, int End, string Text)>();
        foreach (JsonElement f in pred.GetProperty("fields").EnumerateArray())
        {
            int s = f.GetProperty("start").GetInt32();
            int e = f.GetProperty("end").GetInt32();
            TextRange range = view.RangeOf(s, e, out _, out _);
            if (!range.IsPresent)
            {
                unmapped++;
                continue;
            }

            SourceSpan span = ctx.ToSpan(range);
            fields.Add((f.GetProperty("type").GetString()!, span.Start, span.Start + span.Length, f.GetProperty("text").GetString()!));
        }

        o.WriteLine(Json(w =>
        {
            w.WriteStartObject();
            w.WriteString("id", id);
            w.WriteStartArray("fields");
            foreach (var f in fields)
            {
                w.WriteStartObject();
                w.WriteString("type", f.Type);
                w.WriteNumber("start", f.Start);
                w.WriteNumber("end", f.End);
                w.WriteString("text", f.Text);
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }));
        n++;
    }

    Console.WriteLine($"pages {n} unmapped spans {unmapped}");
    return 0;
}

static int Recognizers(string input, string output)
{
    int n = 0;
    using StreamWriter o = new(output, false, new UTF8Encoding(false));
    foreach (JsonElement item in Lines(input))
    {
        string id = item.GetProperty("id").GetString()!;
        string text = item.GetProperty("text").GetString()!;
        var found = new List<(string Type, int Start, int End, string Text)>();
        foreach (var (type, results) in new[]
        {
            ("phone", SequenceRecognizer.RecognizePhoneNumber(text, Culture.English)),
            ("email", SequenceRecognizer.RecognizeEmail(text, Culture.English)),
            ("website", SequenceRecognizer.RecognizeURL(text, Culture.English)),
        })
        {
            foreach (ModelResult r in results)
            {
                // The recognizers report an inclusive end.
                found.Add((type, r.Start, r.End + 1, r.Text));
            }
        }

        o.WriteLine(Json(w =>
        {
            w.WriteStartObject();
            w.WriteString("id", id);
            w.WriteStartArray("fields");
            foreach (var f in found)
            {
                w.WriteStartObject();
                w.WriteString("type", f.Type);
                w.WriteNumber("start", f.Start);
                w.WriteNumber("end", f.End);
                w.WriteString("text", f.Text);
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }));
        n++;
    }

    Console.WriteLine($"items {n}");
    return 0;
}
