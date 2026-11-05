using System.Globalization;

namespace RecursiveAI.OneContact.Ingest.LibPhoneNumberGeo;

/// <summary>
/// Compiles one folder of <c>&lt;language&gt;/&lt;calling code&gt;.txt</c> files (the geocoding or the carrier data) into one prefix map per file,
/// named <c>&lt;table prefix&gt;/&lt;language&gt;/&lt;calling code&gt;</c>. Keys are stored without the calling code, which every key of the file
/// must start with.
/// </summary>
internal sealed class PrefixMapSource(string name, string folder, string tablePrefix, string what) : IIngestSource
{
    public string Name => name;

    public string ToolVersion => "1";

    public string NoticeText => GeoSources.ApacheNotice(what, folder) +
        (folder == "carrier"
            ? "The carrier data is, in the upstream's words, \"based on Wikipedia and communication with carriers\"; it names the carrier a\nnumber range was first assigned to, which may not be its carrier today.\n"
            : string.Empty);

    public void Convert(IngestInput input, TableSetWriter tables)
    {
        // (language, calling code) -> entries, with keys cut of the calling code.
        SortedDictionary<(string Language, string CallingCode), List<(string Key, string Value)>> all = new();
        foreach (PinnedFile file in input.Pin.Files)
        {
            string[] parts = file.Path.Split('/');
            if (parts.Length != 3 || parts[0] != folder || !parts[2].EndsWith(".txt", StringComparison.Ordinal))
            {
                throw new FormatException($"Unexpected pinned path '{file.Path}'.");
            }

            string callingCode = parts[2][..^4];
            if (callingCode.Length is 0 or > 3 || !callingCode.All(char.IsAsciiDigit))
            {
                throw new FormatException($"'{file.Path}' is not named by a calling code.");
            }

            List<(string Key, string Value)> relative = [];
            foreach ((string key, string value) in GeoSources.ReadDataFile(file.Path, input.ReadAllBytes(file.Path)))
            {
                if (!key.StartsWith(callingCode, StringComparison.Ordinal))
                {
                    throw new FormatException($"{file.Path}: key {key} does not start with the calling code.");
                }

                relative.Add((key[callingCode.Length..], value));
            }

            all.Add((parts[1], callingCode), relative);
        }

        foreach (((string language, string callingCode), List<(string Key, string Value)> listed) in all)
        {
            List<(string Key, string Value)> entries = listed;

            // The reference's compiled data keeps the key of a non-English entry that says what the English file says for the same key
            // but not its text (found by comparing answers; see docs/phones-geo.md): the entry is empty, which means "as in English".
            if (language != "en" && all.TryGetValue(("en", callingCode), out List<(string Key, string Value)>? english))
            {
                Dictionary<string, string> inEnglish = new(StringComparer.Ordinal);
                foreach ((string key, string value) in english)
                {
                    inEnglish[key] = value;
                }

                entries = [.. listed.Select(e => inEnglish.TryGetValue(e.Key, out string? en) && en == e.Value ? (e.Key, string.Empty) : e)];
            }

            tables.Add(string.Create(CultureInfo.InvariantCulture, $"{tablePrefix}/{language}/{callingCode}"), PrefixMapWriter.Build(entries));
        }
    }
}
