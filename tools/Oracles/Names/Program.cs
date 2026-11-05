using System.Text;
using System.Text.Json;
using NameParser;
using RecursiveAI.OneContact;
using RecursiveAI.OneContact.Names;

// Usage: oracles-names (ours|nps) <gold.tsv> <out.jsonl>
//
// Writes one JSON object per gold row: {"id", "class", "parts": {"T": [...], ...}, "has_form", "elf"}. Part letters are the gold
// label letters (T title, G given, M middle, F family, S suffix, N nickname, B company base, L legal form).
if (args.Length != 3 || args[0] is not ("ours" or "nps"))
{
    Console.Error.WriteLine("usage: oracles-names (ours|nps) <gold.tsv> <out.jsonl>");
    return 64;
}

bool ours = args[0] == "ours";
using StreamWriter output = new(args[2], false, new UTF8Encoding(false));
foreach (string line in File.ReadLines(args[1]).Skip(1))
{
    string[] f = line.Split('\t');
    string id = f[0];
    string hint = f[2];
    string text = f[4];
    using MemoryStream ms = new();
    using (Utf8JsonWriter w = new(ms))
    {
        w.WriteStartObject();
        w.WriteString("id", id);
        if (ours)
        {
            WriteOurs(w, text, hint);
        }
        else
        {
            WriteNps(w, text);
        }

        w.WriteEndObject();
    }

    output.WriteLine(Encoding.UTF8.GetString(ms.ToArray()));
}

return 0;

static NameHints Hints(string hint)
{
    // "US", "ES/ca" or a bare lower-case language.
    string[] parts = hint.Split('/');
    Region country = Region.Unknown;
    string? language = null;
    foreach (string p in parts)
    {
        if (p.Length == 2 && char.IsUpper(p[0]))
        {
            country = Region.Parse(p);
        }
        else
        {
            language = p;
        }
    }

    return new NameHints(country, language);
}

static void WriteOurs(Utf8JsonWriter w, string text, string hint)
{
    NameHints hints = Hints(hint);
    NameParseResult r = NameParsing.Split(text, hints);
    w.WriteString("class", r.Class.ToString().ToLowerInvariant());
    w.WriteStartObject("parts");
    if (r.Class == NameClass.Company)
    {
        CompanyName c = r.Company;
        Part(w, "B", c.Base.Text, c.TradingAs.Text);
        if (c.LegalForm is not null)
        {
            Part(w, "L", c.LegalForm.Text);
        }
    }
    else
    {
        List<string> t = [], g = [], m = [], fam = [], s = [], n = [];
        foreach (PersonName p in r.Persons)
        {
            t.Add(p.Prefix.Text);
            g.Add(p.Given.Text);
            m.Add(p.Middle.Text);
            fam.Add(p.Particle.Text + " " + p.Family.Text + " " + p.SecondFamily.Text);
            s.Add(p.Suffix.Text + " " + p.ProfessionalSuffix.Text);
            n.Add(p.Nickname.Text);
        }

        Part(w, "T", [.. t]);
        Part(w, "G", [.. g]);
        Part(w, "M", [.. m]);
        Part(w, "F", [.. fam]);
        Part(w, "S", [.. s]);
        Part(w, "N", [.. n]);
    }

    w.WriteEndObject();

    // The legal form is looked for in every string, as cleanco does, so the two can be compared on persons and companies alike.
    CompanyName.TryParse(text, hints, out CompanyName any, out _);
    w.WriteBoolean("has_form", any.LegalForm is not null);
    w.WriteString("elf", any.LegalForm is null ? string.Empty : any.LegalForm.IsAmbiguous ? "?" : any.LegalForm.ElfCode);
}

static void WriteNps(Utf8JsonWriter w, string text)
{
    HumanName name = new(text);
    w.WriteStartObject("parts");
    Part(w, "T", name.Title);
    Part(w, "G", name.First);
    Part(w, "M", name.Middle);
    Part(w, "F", name.Last);
    Part(w, "S", name.Suffix);
    Part(w, "N", name.Nickname);
    w.WriteEndObject();
}

static void Part(Utf8JsonWriter w, string label, params string[] values)
{
    w.WriteStartArray(label);
    foreach (string v in values)
    {
        if (!string.IsNullOrWhiteSpace(v))
        {
            w.WriteStringValue(v.Trim());
        }
    }

    w.WriteEndArray();
}
