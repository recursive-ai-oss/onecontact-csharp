using System.Buffers;
using System.Text;
using System.Text.Json;
using RecursiveAI.OneContact;
using RecursiveAI.OneContact.Addresses.Parsing;
using RecursiveAI.OneContact.Addresses.Streets;
using RecursiveAI.OneContact.Names;

// usage: eval-rules address|name <gold.jsonl> <predictions.jsonl>
//
// Reads an evaluation set (one JSON object per line, as tools/Eval/score.py documents) and writes one prediction per item in the
// scorer's format. With ONECONTACT_EVAL_HINT=1 each item's `country` is passed to the parser as its region hint (the sets are per country);
// without it the parser gets no hint, as the reference parsers do. Values are the text as written in the input (the parts' own text), never the standardized text, because the
// gold values are surface text. Nothing is printed except counts.
if (args.Length != 3 || args[0] is not ("address" or "name"))
{
    Console.Error.WriteLine("usage: eval-rules address|name <gold.jsonl> <predictions.jsonl>");
    return 64;
}

bool hint = Environment.GetEnvironmentVariable("ONECONTACT_EVAL_HINT") == "1";
int n = 0, refused = 0;
using StreamWriter output = new(args[2], false, new UTF8Encoding(false));
foreach (string line in File.ReadLines(args[1]))
{
    if (string.IsNullOrWhiteSpace(line))
    {
        continue;
    }

    using JsonDocument doc = JsonDocument.Parse(line);
    JsonElement root = doc.RootElement;
    string id = root.GetProperty("id").GetString()!;
    string text = root.GetProperty(args[0] == "address" ? "text" : "text").GetString()!;
    string country = root.TryGetProperty("country", out JsonElement c) ? c.GetString()! : string.Empty;
    string lang = root.TryGetProperty("lang", out JsonElement l) ? l.GetString()! : string.Empty;
    using MemoryStream ms = new();
    using (Utf8JsonWriter w = new(ms))
    {
        w.WriteStartObject();
        w.WriteString("id", id);
        if (args[0] == "address")
        {
            if (!WriteAddress(w, text, hint && country.Length == 2 ? Region.Parse(country) : Region.Unknown))
            {
                refused++;
            }
        }
        else
        {
            WriteName(w, text, country, lang);
        }

        w.WriteEndObject();
    }

    output.WriteLine(Encoding.UTF8.GetString(ms.ToArray()));
    n++;
}

Console.WriteLine($"items {n}, address refused {refused}");
return 0;

static bool WriteAddress(Utf8JsonWriter w, string text, Region region)
{
    // The scorer expects blank/absent fields to be omitted.
    Dictionary<string, string> fields = [];
    Dictionary<string, string> parts = [];
    bool ok = RuleAddressParser.TryParse(text, region, out ParsedAddress? a, out _) == OperationStatus.Done && a is not null;
    if (ok)
    {
        List<string> road = [], unit = [], box = [];
        foreach (StreetLine line in a!.DeliveryLines)
        {
            foreach (StreetPart p in line.Parts)
            {
                switch (p.Role)
                {
                    case StreetPartRole.HouseNumber: Add(fields, "house_number", p.Text); break;
                    case StreetPartRole.PreDirectional: Add(parts, "pre_directional", p.Text); road.Add(p.Text); break;
                    case StreetPartRole.StreetName: Add(parts, "street_name", p.Text); road.Add(p.Text); break;
                    case StreetPartRole.StreetType: Add(parts, "street_type", p.Text); road.Add(p.Text); break;
                    case StreetPartRole.PostDirectional: Add(parts, "post_directional", p.Text); road.Add(p.Text); break;
                    case StreetPartRole.UnitPrefix:
                    case StreetPartRole.UnitDesignator:
                    case StreetPartRole.UnitNumber:
                        unit.Add(p.Text);
                        break;
                    case StreetPartRole.BoxDesignator:
                    case StreetPartRole.BoxNumber:
                    case StreetPartRole.RouteDesignator:
                    case StreetPartRole.RouteNumber:
                    case StreetPartRole.GeneralDelivery:
                        box.Add(p.Text);
                        break;
                }
            }
        }

        Add(fields, "road", string.Join(' ', road));
        Add(fields, "unit", string.Join(' ', unit));
        Add(fields, "po_box", string.Join(' ', box));
        Add(fields, "recipient", string.Join(' ', a.LeadingLines.Select(x => x.Text)));
        Add(fields, "city", a.Locality?.Text);
        Add(fields, "state", a.AdminArea?.Text);
        Add(fields, "postcode", a.PostalCode?.Text);
        Add(fields, "country", a.CountryName?.Text);
    }

    WriteMap(w, "fields", fields);
    WriteMap(w, "parts", parts);
    return ok;
}

static void Add(Dictionary<string, string> d, string key, string? value)
{
    if (!string.IsNullOrWhiteSpace(value) && !d.ContainsKey(key))
    {
        d[key] = value.Trim();
    }
}

static void WriteMap(Utf8JsonWriter w, string name, Dictionary<string, string> d)
{
    w.WriteStartObject(name);
    foreach ((string k, string v) in d)
    {
        w.WriteString(k, v);
    }

    w.WriteEndObject();
}

static void WriteName(Utf8JsonWriter w, string text, string country, string lang)
{
    Region region = country.Length == 2 ? Region.Parse(country) : Region.Unknown;
    NameParseResult r = NameParsing.Split(text, new NameHints(region, lang.Length > 0 ? lang : null));
    w.WriteString("class", r.Class.ToString().ToLowerInvariant());
    w.WriteStartObject("parts");
    if (r.Class == NameClass.Company)
    {
        CompanyName c = r.Company;
        Part(w, "B", c.Base.Text);
        if (c.LegalForm is not null)
        {
            Part(w, "L", c.LegalForm.Text);
        }
    }
    else
    {
        List<string> t = [], g = [], m = [], fam = [], s = [], nick = [];
        foreach (PersonName p in r.Persons)
        {
            t.Add(p.Prefix.Text);
            g.Add(p.Given.Text);
            m.Add(p.Middle.Text);
            fam.Add(p.Particle.Text + " " + p.Family.Text + " " + p.SecondFamily.Text);
            s.Add(p.Suffix.Text + " " + p.ProfessionalSuffix.Text);
            nick.Add(p.Nickname.Text);
        }

        Part(w, "T", [.. t]);
        Part(w, "G", [.. g]);
        Part(w, "M", [.. m]);
        Part(w, "F", [.. fam]);
        Part(w, "S", [.. s]);
        Part(w, "N", [.. nick]);
    }

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
