using System.Text.Json;

namespace OneContact.Data.AddressUsCa;

/// <summary>The span rules every written example must satisfy, shared by the generators.</summary>
internal static class SpanCheck
{
    /// <summary>Null when the example is sound, otherwise the first problem found.</summary>
    internal static string? Validate(string line)
    {
        using JsonDocument doc = JsonDocument.Parse(line);
        JsonElement root = doc.RootElement;
        string text = root.GetProperty("text").GetString()!;
        List<(int S, int E)> tokens = root.GetProperty("tokens").EnumerateArray().Select(t => (t[0].GetInt32(), t[1].GetInt32())).ToList();
        HashSet<int> starts = tokens.Select(t => t.S).ToHashSet();
        HashSet<int> ends = tokens.Select(t => t.E).ToHashSet();
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].S >= tokens[i].E || tokens[i].E > text.Length || (i > 0 && tokens[i].S < tokens[i - 1].E))
            {
                return "tokens out of order or outside the text";
            }
        }

        bool[] covered = new bool[text.Length];
        int previousEnd = 0;
        foreach (JsonElement e in root.GetProperty("entities").EnumerateArray())
        {
            int s = e[0].GetInt32();
            int en = e[1].GetInt32();
            string label = e[2].GetString()!;
            if (s < previousEnd)
            {
                return $"span {label} overlaps or is out of order";
            }

            if (s >= en || en > text.Length)
            {
                return $"span {label} outside the text";
            }

            if (!starts.Contains(s) || !ends.Contains(en))
            {
                return $"span {label} does not sit on token edges";
            }

            if (char.IsWhiteSpace(text[s]) || char.IsWhiteSpace(text[en - 1]))
            {
                return $"span {label} starts or ends in a space";
            }

            for (int k = s; k < en; k++)
            {
                covered[k] = true;
            }

            previousEnd = en;
        }

        for (int k = 0; k < text.Length; k++)
        {
            if (char.IsLetterOrDigit(text[k]) && !covered[k])
            {
                return "a letter or digit outside every span";
            }
        }

        return null;
    }
}
