using System.Text;

namespace RecursiveAI.OneContact.Ingest.LibPostalDictionaries;

/// <summary>
/// Turns the pinned libpostal dictionary files (<c>&lt;language&gt;/&lt;dictionary&gt;.txt</c>) into one compiled table per file, named
/// <c>&lt;language&gt;/&lt;dictionary&gt;</c>. The upstream format is one entry per line, the canonical form first and its variants after it,
/// separated by <c>|</c>. A table is the same entries as UTF-8 text with one entry per line and the phrases separated by a tab:
/// phrases are lower-cased with the invariant culture, runs of white space collapse to one space, empty phrases and repeated
/// phrases within an entry are dropped, and empty entries are dropped. Entry order is the upstream order. See docs/address-dictionaries.md.
/// </summary>
public sealed class LibPostalDictionariesSource : IIngestSource
{
    /// <inheritdoc/>
    public string Name => "libpostal-dictionaries";

    /// <inheritdoc/>
    public string ToolVersion => "1";

    /// <inheritdoc/>
    public string NoticeText =>
        "The address expansion tables are compiled from the dictionary files of libpostal (https://github.com/openvenues/libpostal),\n" +
        "tag v1.1.4, under the MIT licence.\n\n" +
        "The MIT License (MIT)\n\n" +
        "Copyright (c) 2015 openvenues\n\n" +
        "Permission is hereby granted, free of charge, to any person obtaining a copy\n" +
        "of this software and associated documentation files (the \"Software\"), to deal\n" +
        "in the Software without restriction, including without limitation the rights\n" +
        "to use, copy, modify, merge, publish, distribute, sublicense, and/or sell\n" +
        "copies of the Software, and to permit persons to whom the Software is\n" +
        "furnished to do so, subject to the following conditions:\n\n" +
        "The above copyright notice and this permission notice shall be included in all\n" +
        "copies or substantial portions of the Software.\n\n" +
        "THE SOFTWARE IS PROVIDED \"AS IS\", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR\n" +
        "IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,\n" +
        "FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE\n" +
        "AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER\n" +
        "LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,\n" +
        "OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE\n" +
        "SOFTWARE.\n";

    /// <inheritdoc/>
    public void Convert(IngestInput input, TableSetWriter tables)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(tables);
        foreach (string path in input.Pin.Files.Select(f => f.Path).Order(StringComparer.Ordinal))
        {
            if (!path.EndsWith(".txt", StringComparison.Ordinal))
            {
                continue;
            }

            string text = Encoding.UTF8.GetString(input.ReadAllBytes(path));
            StringBuilder table = new();
            foreach (string raw in text.Split('\n'))
            {
                List<string> phrases = [];
                foreach (string piece in raw.TrimEnd('\r').Split('|'))
                {
                    string phrase = string.Join(' ', piece.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                    if (phrase.Length > 0 && !phrases.Contains(phrase))
                    {
                        phrases.Add(phrase);
                    }
                }

                if (phrases.Count > 0)
                {
                    table.Append(string.Join('\t', phrases)).Append('\n');
                }
            }

            tables.Add(path[..^4], Encoding.UTF8.GetBytes(table.ToString()));
        }
    }
}
