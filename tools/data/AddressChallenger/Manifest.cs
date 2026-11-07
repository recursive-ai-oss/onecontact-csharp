using System.Globalization;
using System.Text;
using System.Text.Json;

namespace OneContact.Data.AddressChallenger;

/// <summary>Writes manifest.json: the lineage, the pinned input, the seed and salts, the label mapping, every output file with its role, line count and SHA-256, and the counts.</summary>
internal static class Manifest
{
    internal static void Write(string path, Dictionary<string, string> o, ulong seed, int devPerMille, Stats stats, string outDir, string selectionPath, long duplicates, long inputLines, string[] countries)
    {
        using FileStream fs = File.Create(path);
        using Utf8JsonWriter w = new(fs, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        w.WriteStartObject();
        w.WriteString("tool", "AddressChallenger");
        w.WriteString("converterVersion", Converter.Version);
        w.WriteString("set", Program.SetName);
        w.WriteString("lineage", "challenger: OpenStreetMap addresses (ODbL 1.0) as tagged by the published libpostal parser training set. Never read by the default models' recipes; never in a default model package.");
        w.WriteString("attribution", "Contains information from OpenStreetMap, which is made available under the Open Database License (ODbL). (c) OpenStreetMap contributors, https://www.openstreetmap.org/copyright");
        w.WriteString("licence", "Open Data Commons Open Database License 1.0 (https://opendatacommons.org/licenses/odbl/1-0/). This converted data is a Derivative Database: it is kept internal; any publication of it is under the ODbL.");
        w.WriteNumber("seed", seed);
        w.WriteString("split", $"dev when hash(line id, split salt) mod 1000 < {devPerMille}; the rest is train. Fixed from the line id alone, before conversion.");
        w.WriteString("selection", "a line is converted when hash(line id, selection salt) is below its country's rate in selection.tsv; a line whose tagged text repeats one already kept in the country is dropped");
        w.WriteString("selectionSha256", Program.Sha256OfFile(selectionPath));
        w.WriteNumber("selectSalt", Program.SelectSalt);
        w.WriteNumber("splitSalt", Program.SplitSalt);
        w.WriteNumber("inputLines", inputLines);
        w.WriteNumber("droppedDuplicates", duplicates);

        w.WritePropertyName("input");
        w.WriteStartArray();
        string pin = Path.Combine(AppContext.BaseDirectory, "pins", "challenger-osm.pin");
        foreach (string line in File.ReadLines(pin).Where(l => l.StartsWith("file ", StringComparison.Ordinal)))
        {
            string[] c = line.Split(' ');
            w.WriteStartObject();
            w.WriteString("name", c[1]);
            w.WriteNumber("bytes", long.Parse(c[2], CultureInfo.InvariantCulture));
            w.WriteString("sha256", c[3]);
            w.WriteString("url", c[4]);
            w.WriteString("sha1PublishedByHost", c[5]);
            w.WriteEndObject();
        }

        w.WriteEndArray();

        w.WritePropertyName("labelMapping");
        w.WriteStartObject();
        foreach ((string source, string ours) in Converter.MappingTable())
        {
            w.WriteString(source, ours);
        }

        w.WriteEndObject();

        w.WritePropertyName("files");
        w.WriteStartArray();
        long totalLines = 0;
        foreach (string file in Directory.GetFiles(outDir, "*.jsonl").Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileName(file);
            long n = 0;
            foreach (string _ in File.ReadLines(file))
            {
                n++;
            }

            totalLines += n;
            w.WriteStartObject();
            w.WriteString("name", name);
            w.WriteString("role", name.EndsWith(".dev.jsonl", StringComparison.Ordinal) ? "dev" : "train");
            w.WriteString("set", Program.SetName);
            w.WriteNumber("lines", n);
            w.WriteNumber("bytes", new FileInfo(file).Length);
            w.WriteString("sha256", Program.Sha256OfFile(file));
            w.WriteEndObject();
        }

        w.WriteEndArray();
        w.WriteNumber("totalExamples", totalLines);

        w.WritePropertyName("counts");
        w.WriteStartObject();
        foreach (KeyValuePair<string, long> kv in stats.Lines)
        {
            w.WriteNumber(kv.Key, kv.Value);
        }

        w.WriteEndObject();
        foreach (KeyValuePair<string, SortedDictionary<string, long>> g in stats.Groups)
        {
            w.WritePropertyName(g.Key);
            w.WriteStartObject();
            foreach (KeyValuePair<string, long> kv in g.Value)
            {
                w.WriteNumber(kv.Key, kv.Value);
            }

            w.WriteEndObject();
        }

        w.WriteStartArray("countries");
        foreach (string c in countries)
        {
            w.WriteStringValue(c);
        }

        w.WriteEndArray();
        w.WriteEndObject();
    }
}
