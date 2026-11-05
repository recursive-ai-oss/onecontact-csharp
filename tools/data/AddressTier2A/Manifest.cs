using System.Text;
using System.Text.Json;

namespace OneContact.Data.AddressTier2A;

/// <summary>Writes manifest.json: seed, renderer version, the pinned register files, every output file with its role, line count and SHA-256, and the counts.</summary>
internal static class Manifest
{
    // Per register: the cache name prefix, the licence as read at the publisher, and the attribution line the model notices carry.
    private static readonly (string Prefix, string Licence, string Attribution)[] Registers =
    [
        ("ban", "Licence Ouverte 2.0 (Etalab)", "Source : Base Adresse Nationale (BAN), licence ouverte 2.0."),
        ("best", "Creative Commons Attribution 4.0 (CC BY 4.0)", "BeST Address, FPS Policy and Support (BOSA), from the address registers of the Brussels-Capital Region (Paradigm), Flanders (Digitaal Vlaanderen) and Wallonia (SPW); CC BY 4.0."),
        ("bag", "CC0 1.0", "Basisregistratie Adressen en Gebouwen (BAG), Kadaster; CC0 1.0."),
        ("lu", "Creative Commons CC0", "Adresses géoréférencées (BD-L-TC), Administration du cadastre et de la topographie, data.public.lu; CC0."),
        ("at", "CC BY 4.0 with the BEV's attribution condition", "© Österreichisches Adressregister, Stichtagsdaten vom 01.10.2026 (BEV); CC BY 4.0."),
        ("swisstopo", "swisstopo free geodata terms of use (use, processing and commercial use allowed, source reference mandatory)", "Quelle: Bundesamt für Landestopografie swisstopo, Amtliches Verzeichnis der Gebäudeadressen."),
        ("de-nrw", "Datenlizenz Deutschland - Zero - Version 2.0", "Geobasis NRW, Gebäudereferenzen, dl-de/zero-2-0."),
    ];

    internal static void Write(string path, Dictionary<string, string> o, ulong seed, int devPerMille, Renderer renderer, Stats stats, string outDir, string selectionPath, long duplicates, string dataRoot)
    {
        string pinDir = Path.Combine(AppContext.BaseDirectory, "pins");
        using FileStream fs = File.Create(path);
        using Utf8JsonWriter w = new(fs, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        w.WriteStartObject();
        w.WriteString("tool", "AddressTier2A");
        w.WriteString("rendererVersion", Renderer.Version);
        w.WriteNumber("seed", seed);
        w.WriteString("split", $"dev when hash(record id, split salt) mod 1000 < {devPerMille}; the rest is train. Fixed before rendering, from the register record id only.");
        w.WriteString("selection", "a record is rendered when hash(record id, selection salt) is below its region's rate in selection.tsv; a record whose address repeats one already kept is dropped");
        w.WriteString("selectionSha256", Renderer.Sha256Of(selectionPath));
        w.WriteString("templatesSha256", renderer.Sha256);
        w.WriteNumber("droppedDuplicates", duplicates);
        w.WriteString("syntheticParts", "Monaco is synthesized entirely (Monaco.cs, fixed hash draw); North Rhine-Westphalia postcodes are synthetic (the register has none): two leading digits from the municipality inside NRW's real ranges, three from the street");
        w.WritePropertyName("rates");
        JsonSerializer.Serialize(w, renderer.Rates.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(renderer.Rates)));
        w.WritePropertyName("registers");
        w.WriteStartArray();
        foreach ((string prefix, string licence, string attribution) in Registers)
        {
            string pin = Path.Combine(pinDir, (prefix == "swisstopo" ? "swisstopo" : prefix) + ".pin");
            if (!File.Exists(pin) || Directory.GetDirectories(dataRoot, prefix + "-*").Length == 0)
            {
                continue;
            }

            w.WriteStartObject();
            w.WriteString("register", prefix);
            w.WriteString("licence", licence);
            w.WriteString("attribution", attribution);
            w.WritePropertyName("files");
            w.WriteStartArray();
            foreach (string line in File.ReadLines(pin).Where(l => l.StartsWith("file ", StringComparison.Ordinal)))
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
            w.WriteString("sha256", Renderer.Sha256Of(file));
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
