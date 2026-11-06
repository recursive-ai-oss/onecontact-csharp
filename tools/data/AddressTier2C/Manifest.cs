using System.Text;
using System.Text.Json;

namespace OneContact.Data.AddressTier2C;

/// <summary>Writes manifest.json: seed, renderer version, the pinned register files, every output file with its role, line count and SHA-256, and the counts.</summary>
internal static class Manifest
{
    // Per register: the cache name prefix, the licence as read at the publisher, and the attribution line the model notices carry.
    private static readonly (string Prefix, string Licence, string Attribution)[] Registers =
    [
        ("no-matrikkelen", "Creative Commons Attribution 4.0 International (CC BY 4.0)", "Matrikkelen - Adresse, Kartverket; CC BY 4.0."),
        ("cz-ruian", "Creative Commons Attribution 4.0 (CC BY 4.0)", "RÚIAN, adresní místa (CSV výměnný formát), Český úřad zeměměřický a katastrální; CC BY 4.0."),
        ("lv-varis", "CC-BY-4.0", "Valsts adrešu reģistra informācijas sistēma (VARIS), Valsts zemes dienests, atvērtie dati; CC BY 4.0."),
        ("ee-ads", "Maa- ja Ruumiameti avatud ruumiandmete litsents (free for any lawful use with the data source named)", "Aadressiandmete süsteem (ADS), Maa- ja Ruumiamet, 2026."),
        ("mx-denue", "Términos de Libre Uso de la Información del INEGI (commercial use allowed, source named)", "Fuente: INEGI, Directorio Estadístico Nacional de Unidades Económicas, DENUE 05/2026."),
        ("pl-prg", "Dane PRG są dostępne bezpłatnie i do dowolnego wykorzystania (GUGiK, geoportal.gov.pl)", "Państwowy Rejestr Granic, punkty adresowe, Główny Urząd Geodezji i Kartografii, 2026."),
        ("si-rpe", "Creative Commons Attribution 4.0 International (CC BY 4.0)", "Register prostorskih enot, Geodetska uprava Republike Slovenije; CC BY 4.0."),
        ("sk-ra", "CC0 (terms of use of the dataset on data.gov.sk)", "Register adries, Ministerstvo vnútra Slovenskej republiky (CC0)."),
        ("lt-ar", "Creative Commons Attribution 4.0 International (CC BY 4.0)", "Adresų registras, VĮ Registrų centras (open data via geoportal.lt); CC BY 4.0."),
    ];

    internal static void Write(string path, Dictionary<string, string> o, ulong seed, int devPerMille, Renderer renderer, Stats stats, string outDir, string selectionPath, long duplicates, string dataRoot)
    {
        string pinDir = Path.Combine(AppContext.BaseDirectory, "pins");
        using FileStream fs = File.Create(path);
        using Utf8JsonWriter w = new(fs, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        w.WriteStartObject();
        w.WriteString("tool", "AddressTier2C");
        w.WriteString("rendererVersion", Renderer.Version);
        w.WriteNumber("seed", seed);
        w.WriteString("split", $"dev when hash(record id, split salt) mod 1000 < {devPerMille}; the rest is train. Fixed before rendering, from the register record id only.");
        w.WriteString("selection", "a record is rendered when hash(record id, selection salt) is below its region's rate in selection.tsv; a record whose address repeats one already kept is dropped");
        w.WriteString("selectionSha256", Renderer.Sha256Of(selectionPath));
        w.WriteString("templatesSha256", renderer.Sha256);
        w.WriteNumber("droppedDuplicates", duplicates);
        w.WritePropertyName("rates");
        JsonSerializer.Serialize(w, renderer.Rates.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(renderer.Rates)));
        w.WritePropertyName("registers");
        w.WriteStartArray();
        foreach ((string prefix, string licence, string attribution) in Registers)
        {
            string pin = Path.Combine(pinDir, (prefix.Split('-')[0]) + ".pin");
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
