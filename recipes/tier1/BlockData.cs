using System.Text.Json;
using RecursiveAI.OneContact.Models.Tier1;
using RecursiveAI.OneNlp;
using RecursiveAI.OneNlp.Data;

namespace OneContact.Recipe.Tier1;

/// <summary>
/// Reads the rendered contact blocks (JSON Lines: text, labelled field spans with the record each belongs to) into examples for the
/// contact-span head. The first field of each record carries the start suffix, which is how the head learns where blocks begin.
/// </summary>
internal static partial class Data
{
    /// <summary>Reads block examples. A line is kept when its hash falls in the wanted share (see <c>SelectLines</c>), so the same arguments give the same examples.</summary>
    internal static List<Example> ReadBlocks(IEnumerable<string> paths, StringStore strings, int count, uint salt)
    {
        List<Example> result = [];
        foreach (string line in SelectLines(paths, count, salt))
        {
            using JsonDocument json = JsonDocument.Parse(line);
            JsonElement root = json.RootElement;
            string text = root.GetProperty("text").GetString()!;
            var spans = new List<(int S, int E, string L, int R)>();
            foreach (JsonElement s in root.GetProperty("spans").EnumerateArray())
            {
                spans.Add((s[0].GetInt32(), s[1].GetInt32(), s[2].GetString()!, s[3].GetInt32()));
            }

            result.Add(BuildBlock(text, spans, strings));
        }

        return result;
    }

    private static Example BuildBlock(string text, List<(int S, int E, string L, int R)> spans, StringStore strings)
    {
        spans.Sort((a, b) => a.S.CompareTo(b.S));
        var cuts = new HashSet<int>();
        foreach ((int s, int e, _, _) in spans)
        {
            cuts.Add(s);
            cuts.Add(e);
        }

        List<(int Start, int Length)> words = Tier1Tokenizer.Tokenize(text, cuts);
        var boundaries = new TokenBoundary[words.Count];
        for (int i = 0; i < boundaries.Length; i++)
        {
            boundaries[i] = new TokenBoundary(words[i].Start, words[i].Length);
        }

        var doc = new Doc(strings, text, boundaries);
        var seenRecords = new HashSet<int>();
        var specs = new List<SpanSpec>();
        int previousEnd = 0;
        int token = 0;
        foreach ((int s, int e, string label, int record) in spans)
        {
            while (token < words.Count && words[token].Start < s)
            {
                token++;
            }

            int first = token;
            int last = first;
            while (last < words.Count && words[last].Start + words[last].Length <= e)
            {
                last++;
            }

            if (first >= words.Count || last <= first || first < previousEnd)
            {
                continue;
            }

            // The first field of a record, in text order, starts its block.
            string tag = record >= 0 && seenRecords.Add(record) ? label + Tier1Pipeline.StartSuffix : label;
            specs.Add(new SpanSpec(first, last, tag));
            previousEnd = last;
            token = last;
        }

        doc.SetEntities(specs.ToArray());
        return Example.FromReference(doc);
    }
}
