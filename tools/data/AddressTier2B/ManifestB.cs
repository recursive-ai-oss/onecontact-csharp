using System.Text.Json;

namespace OneContact.Data.AddressTier2B;

/// <summary>Writes manifest.json: seed, renderer version, the pinned source files, every output file with its role, line count and SHA-256, and the counts.</summary>
internal static class ManifestB
{
    // Per source: the cache name prefix, the licence as read at the publisher, and the attribution line the model notices carry.
    private static readonly (string Prefix, string Licence, string Attribution)[] Sources =
    [
        ("osopennames", "Open Government Licence v3.0 (OS OpenData)", "Contains OS data © Crown copyright and database right 2026. Contains Royal Mail data © Royal Mail copyright and database right 2026. Contains National Statistics data © Crown copyright and database right 2026."),
        ("catastro", "Free reuse with attribution to the Dirección General del Catastro (licence compatible with CC BY 4.0)", "Fuente: Dirección General del Catastro (Ministerio de Hacienda), INSPIRE Direcciones."),
        ("anncsu", "Creative Commons Attribution 4.0 (CC BY 4.0)", "Archivio Nazionale dei Numeri Civici e delle Strade Urbane (ANNCSU), Agenzia delle Entrate e Istat; CC BY 4.0."),
        ("istat", "Creative Commons Attribution 4.0 (CC BY 4.0)", "Istat, Elenco dei comuni italiani; CC BY 4.0."),
        ("geonames", "Creative Commons Attribution 4.0 (CC BY 4.0)", "GeoNames postal codes, www.geonames.org; CC BY 4.0."),
    ];

    internal static void Write(string path, ulong seed, int devPerMille, RendererB renderer, Stats stats, string outDir, string selectionPath, long duplicates, string dataRoot)
    {
        string pinDir = Path.Combine(AppContext.BaseDirectory, "pins");
        using FileStream fs = File.Create(path);
        using Utf8JsonWriter w = new(fs, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        w.WriteStartObject();
        w.WriteString("tool", "AddressTier2B");
        w.WriteString("rendererVersion", RendererB.Version);
        w.WriteNumber("seed", seed);
        w.WriteString("split", $"dev when hash(record id, split salt) mod 1000 < {devPerMille}; the rest is train. Fixed before rendering, from the record id only.");
        w.WriteString("selection", "a record is rendered when hash(record id, selection salt) is below its region's rate in selection.tsv; outside the synthetic countries a record whose address repeats one already kept is dropped");
        w.WriteString("selectionSha256", RendererB.Sha256Of(selectionPath));
        w.WriteString("namesTemplateSha256", renderer.NamesSha256);
        w.WriteString("streetsTemplateSha256", renderer.StreetsSha256);
        w.WriteNumber("droppedDuplicates", duplicates);
        w.WriteString("syntheticParts", "GB: each road of OS Open Names is paired with the nearest postcode units of its postcode district in its 10 km tile (a plausible, not a certain, postcode); house numbers are drawn. ES: none (Catastro portals are real; floors and doors are drawn). IT: the CAP is looked up by comune in the GeoNames list, and picked by a fixed hash where a comune has several; house numbers are real. PT, IE, MT, SM, VA, AD, JE, GG, IM: the postcode areas and place names are real (GeoNames), the streets come from our own vocabulary (tier2b-streets.tsv), and the house number and, for IE, MT, JE, GG and IM, the last part of the postcode are drawn");
        w.WritePropertyName("rates");
        JsonSerializer.Serialize(w, renderer.Rates.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(renderer.Rates)));
        w.WritePropertyName("registers");
        w.WriteStartArray();
        foreach ((string prefix, string licence, string attribution) in Sources)
        {
            string pin = Path.Combine(pinDir, prefix + ".pin");
            if (!File.Exists(pin) || Directory.GetDirectories(dataRoot, prefix + "-*").Length == 0)
            {
                continue;
            }

            w.WriteStartObject();
            w.WriteString("register", prefix);
            w.WriteString("licence", licence);
            w.WriteString("attribution", attribution);
            string[] files = File.ReadLines(pin).Where(l => l.StartsWith("file ", StringComparison.Ordinal)).ToArray();
            w.WriteString("pinSha256", RendererB.Sha256Of(pin));
            w.WriteNumber("fileCount", files.Length);
            if (files.Length <= 40)
            {
                w.WritePropertyName("files");
                w.WriteStartArray();
                foreach (string line in files)
                {
                    string[] c = line.Split(' ');
                    w.WriteStartObject();
                    w.WriteString("name", c[1]);
                    w.WriteNumber("bytes", long.Parse(c[2], System.Globalization.CultureInfo.InvariantCulture));
                    w.WriteString("sha256", c[3]);
                    w.WriteString("url", c[4]);
                    w.WriteEndObject();
                }

                w.WriteEndArray();
            }

            w.WriteEndObject();
        }

        w.WriteEndArray();
        w.WritePropertyName("files");
        w.WriteStartArray();
        foreach (string file in Directory.GetFiles(outDir, "*.jsonl").OrderBy(f => f, StringComparer.Ordinal))
        {
            string name = Path.GetFileName(file);
            w.WriteStartObject();
            w.WriteString("name", name);
            w.WriteString("role", name.Contains(".dev.", StringComparison.Ordinal) ? "dev" : "train");
            w.WriteNumber("examples", File.ReadLines(file).LongCount());
            w.WriteString("sha256", RendererB.Sha256Of(file));
            w.WriteEndObject();
        }

        w.WriteEndArray();
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

        w.WriteEndObject();
    }
}
