using System.Globalization;
using System.Text.Json;
using RecursiveAI.OneContact.Models.Accurate;
using RecursiveAI.OneNlp;
using RecursiveAI.OneNlp.Data;

namespace OneContact.Recipe.Accurate;

/// <summary>
/// Reads the rendered training sets (JSON Lines) into OneNLP examples. A set is read in file order and every line whose id hashes into the
/// wanted share is kept, so the same arguments always give the same examples, which is what makes a resumed run continue the same training.
/// </summary>
internal static partial class Data
{
    // The labels of the address head. The data sets use these names for fields; "O" is no field.
    internal const string Outside = "O";

    /// <summary>Reads address examples: words as supplied by the set, a word for every line break, B/I tags in the Tag column.</summary>
    internal static List<Example> ReadAddresses(string path, StringStore strings, int count, uint salt, Func<string, string>? relabel = null)
    {
        List<Example> result = [];
        foreach (string line in SelectLines(path, count, salt))
        {
            using JsonDocument json = JsonDocument.Parse(line);
            JsonElement root = json.RootElement;
            string text = root.GetProperty("text").GetString()!;
            var entities = new List<(int S, int E, string L)>();
            foreach (JsonElement e in root.GetProperty("entities").EnumerateArray())
            {
                entities.Add((e[0].GetInt32(), e[1].GetInt32(), relabel is null ? e[2].GetString()! : relabel(e[2].GetString()!)));
            }

            // The set already cut words at the edges of its spans; the supplied words are used as they are, with line breaks added.
            var words = new List<(int Start, int Length)>();
            foreach (JsonElement t in root.GetProperty("tokens").EnumerateArray())
            {
                words.Add((t[0].GetInt32(), t[1].GetInt32() - t[0].GetInt32()));
            }

            result.Add(Build(text, words, entities, strings, classLabel: null));
        }

        return result;
    }

    /// <summary>Reads name and company examples: part tags in the Pos column, the class on every word in the Morph column.</summary>
    internal static List<Example> ReadNames(IEnumerable<string> paths, StringStore strings, int count, uint salt)
    {
        List<Example> result = [];
        foreach (string line in SelectLines(paths, count, salt))
        {
            using JsonDocument json = JsonDocument.Parse(line);
            JsonElement root = json.RootElement;
            string text = root.GetProperty("text").GetString()!;
            string cls = root.GetProperty("class").GetString()!;
            var entities = new List<(int S, int E, string L)>();
            foreach (JsonElement p in root.GetProperty("parts").EnumerateArray())
            {
                entities.Add((p[1].GetInt32(), p[2].GetInt32(), p[0].GetString()!));
            }

            var cuts = new HashSet<int>();
            foreach ((int s, int e, _) in entities)
            {
                cuts.Add(s);
                cuts.Add(e);
            }

            List<(int Start, int Length)> words = AccurateTokenizer.Tokenize(text, cuts);
            result.Add(Build(text, words, entities, strings, classLabel: cls));
        }

        return result;
    }

    // Words as the set supplied them plus a word for every line break; each labelled stretch becomes entities over its words, one per line it touches.
    private static Example Build(string text, List<(int Start, int Length)> supplied, List<(int S, int E, string L)> entities, StringStore strings, string? classLabel)
    {
        var words = new List<(int Start, int Length)>(supplied.Count + 4);
        int previousEnd = 0;
        foreach ((int start, int length) in supplied)
        {
            AddBreaks(text, previousEnd, start, words);
            words.Add((start, length));
            previousEnd = start + length;
        }

        AddBreaks(text, previousEnd, text.Length, words);

        var boundaries = new TokenBoundary[words.Count];
        for (int i = 0; i < boundaries.Length; i++)
        {
            boundaries[i] = new TokenBoundary(words[i].Start, words[i].Length);
        }

        var doc = new Doc(strings, text, boundaries);
        entities.Sort((a, b) => a.S.CompareTo(b.S));
        var specs = new List<SpanSpec>();
        int entity = 0;
        int runStart = -1;
        string? runLabel = null;
        int last = -1;
        for (int i = 0; i < words.Count; i++)
        {
            (int start, int length) = words[i];
            string? label = null;
            if (text[start] != '\n')
            {
                while (entity < entities.Count && entities[entity].E <= start)
                {
                    entity++;
                }

                if (entity < entities.Count && entities[entity].S <= start && start + length <= entities[entity].E)
                {
                    label = entities[entity].L;
                }
            }

            // A run ends at a word with another label (or none), at a line break, or where a different entity of the same label begins.
            bool sameEntity = label is not null && runLabel == label && last == entity;
            if (runLabel is not null && !sameEntity)
            {
                specs.Add(new SpanSpec(runStart, i, runLabel));
                runLabel = null;
            }

            if (label is not null && runLabel is null)
            {
                runStart = i;
                runLabel = label;
                last = entity;
            }
        }

        if (runLabel is not null)
        {
            specs.Add(new SpanSpec(runStart, words.Count, runLabel));
        }

        doc.SetEntities(specs.ToArray());
        return classLabel is null
            ? Example.FromReference(doc)
            : Example.FromReference(doc, new Dictionary<string, float>(StringComparer.Ordinal) { [classLabel] = 1f });
    }

    private static void AddBreaks(string text, int from, int to, List<(int Start, int Length)> words)
    {
        for (int i = from; i < to; i++)
        {
            if (text[i] == '\n')
            {
                words.Add((i, 1));
            }
        }
    }

    private static IEnumerable<string> SelectLines(string path, int count, uint salt) => SelectLines([path], count, salt);

    // Keeps a line when the hash of its text, mixed with the salt, falls in the wanted share. The share is found by a first pass that counts lines.
    private static IEnumerable<string> SelectLines(IEnumerable<string> paths, int count, uint salt)
    {
        string[] files = [.. paths];
        long total = 0;
        foreach (string file in files)
        {
            total += File.ReadLines(file).LongCount();
        }

        double share = Math.Min(1.0, (double)count / Math.Max(1, total));
        long kept = 0;
        foreach (string file in files)
        {
            foreach (string line in File.ReadLines(file))
            {
                if (kept >= count)
                {
                    yield break;
                }

                if (Hash(line, salt) < share)
                {
                    kept++;
                    yield return line;
                }
            }
        }
    }

    // FNV-1a over the UTF-16 units, finished with a mix, mapped to [0, 1).
    private static double Hash(string s, uint salt)
    {
        ulong h = 14695981039346656037UL ^ salt;
        foreach (char c in s)
        {
            h = (h ^ c) * 1099511628211UL;
        }

        h ^= h >> 33;
        h *= 0xFF51AFD7ED558CCDUL;
        h ^= h >> 33;
        return (h >> 11) * (1.0 / (1UL << 53));
    }

    internal static string Describe(IReadOnlyList<Example> examples) =>
        string.Create(CultureInfo.InvariantCulture, $"{examples.Count} examples, {examples.Sum(e => e.Predicted.Length)} words");
}
