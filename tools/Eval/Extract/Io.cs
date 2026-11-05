using System.Text;
using System.Text.Json;
using RecursiveAI.OneContact.Extraction.Markup;
using RecursiveAI.OneContact.Extraction.Text;
using RecursiveAI.OneHtml;

namespace OneContact.Eval.Extract;

/// <summary>File and view helpers shared by the commands.</summary>
internal static class Io
{
    public static IEnumerable<JsonElement> Lines(string path)
    {
        foreach (string line in File.ReadLines(path))
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                yield return JsonDocument.Parse(line).RootElement;
            }
        }
    }

    /// <summary>The stored pages of a set; a page that was fetched twice counts once.</summary>
    public static IEnumerable<JsonElement> Pages(string dir)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return Lines(Path.Combine(dir, "pages.jsonl")).Where(p => seen.Add(p.GetProperty("id").GetString()!));
    }

    public static string Json(Action<Utf8JsonWriter> write)
    {
        using MemoryStream ms = new();
        using (Utf8JsonWriter w = new(ms, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            write(w);
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    public static (PageContext Page, TextView View) ViewOf(string dir, JsonElement page)
    {
        string id = page.GetProperty("id").GetString()!;
        byte[] bytes = File.ReadAllBytes(Path.Combine(dir, "pages", id + ".html"));
        Document doc = Document.Parse(bytes, new ParseOptions { ContentType = page.GetProperty("ctype").GetString() });
        var ctx = new PageContext(doc, null, bytesInput: true);
        return (ctx, VisibleTextReader.Read(ctx, out _));
    }
}
