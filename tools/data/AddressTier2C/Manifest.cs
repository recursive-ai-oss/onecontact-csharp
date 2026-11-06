using System.Text;
using System.Text.Json;

using Nordic = OneContact.Data.AddressTier2C.Registers;

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
        ("fi-dvv", "Creative Commons Attribution 4.0 International (CC BY 4.0)", "Rakennusten osoitetiedot, Digi- ja väestötietovirasto (Väestörekisterikeskus), CC BY 4.0."),
        ("is-stadfangaskra", "Creative Commons Attribution (CC BY), as listed by the publisher's open data catalogue entry", "Staðfangaskrá, HMS / Þjóðskrá Íslands, CC BY."),
        ("nordic-geonames", "Creative Commons Attribution 4.0 (CC BY 4.0)", "GeoNames postal codes, www.geonames.org; CC BY 4.0."),
    ];

    private const int SyntheticPerCountry = Nordic.SyntheticRecords;
    // Croatia, Hungary, Romania and Australia (renderer file RendererC3.cs).
    private static readonly (string Prefix, string Licence, string Attribution)[] RegistersC3 =
    [
        ("hr-dgu", "Otvorena dozvola (Croatian open licence: commercial and non-commercial use with the source named)", "Adrese (INSPIRE), Državna geodetska uprava; contains public sector information under the Otvorena dozvola."),
        ("hu-geonames", "Creative Commons Attribution 4.0 (CC BY 4.0)", "GeoNames postal codes, geonames.org; CC BY 4.0. Street names are our own."),
        ("ro-posta", "Open Government Licence Romania v1.0 (OGL-ROU-1.0)", "Coduri poștale România (Infocod, Sept. 2016), Poșta Română, via data.gov.ro; contains public information under the Open Government Licence v1.0."),
        ("au-abs", "Creative Commons Attribution 4.0 International (CC BY 4.0)", "Australian Bureau of Statistics, ASGS Edition 3 (2021) geographic correspondences, CG_SAL_2021_POA_2021; CC BY 4.0."),
        ("au-geonames", "Creative Commons Attribution 4.0 (CC BY 4.0)", "GeoNames postal codes, geonames.org; CC BY 4.0. Street names are our own."),
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
        w.WriteString("rendererC3Version", RendererC3.Version);
        w.WriteString("templatesC3NamesSha256", Renderer.Sha256Of(Path.Combine(AppContext.BaseDirectory, "templates", "tier2c3-names.tsv")));
        w.WriteString("templatesC3StreetsSha256", Renderer.Sha256Of(Path.Combine(AppContext.BaseDirectory, "templates", "tier2c3-streets.tsv")));
        w.WriteNumber("droppedDuplicates", duplicates);
        w.WriteString("syntheticParts", $"DK, SE: the postcode areas and place names are real (GeoNames); the street names are drawn from our own vocabulary (tier2c-streets-nordic.tsv, {SyntheticPerCountry} records per country before sampling) and the house number and letter are drawn. FI: the register is real; bilingual addresses are used in Finnish only. IS: the register is real");
        w.WritePropertyName("rates");
        JsonSerializer.Serialize(w, renderer.Rates.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(renderer.Rates)));
        w.WritePropertyName("registers");
        w.WriteStartArray();
        foreach ((string prefix, string licence, string attribution) in Registers.Concat(RegistersC3))
        {
            string pin = Path.Combine(pinDir, prefix + ".pin");
            if (!File.Exists(pin))
            {
                pin = Path.Combine(pinDir, prefix.Split('-')[0] + ".pin");
            }

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
