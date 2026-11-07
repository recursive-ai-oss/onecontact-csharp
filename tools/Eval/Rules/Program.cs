using System.Buffers;
using System.Text;
using System.Text.Json;
using RecursiveAI.OneContact;
using RecursiveAI.OneContact.Addresses.Parsing;
using RecursiveAI.OneContact.Addresses.Streets;
using RecursiveAI.OneContact.Names;

// usage: eval-rules address|name|diag <gold.jsonl> <predictions.jsonl> [hint]
//
// With `hint`, an address is parsed with its item's country as the region hint (the optional hint the happy path also accepts);
// without it the parser infers the country from the text.
//
// `diag` writes, per item, the Tier 2 countries that read the text when given as the hint, grouped by identical fields, and the labels in which the groups differ:
// counts for the outcome tables, never the text.
//
// Reads an evaluation set (one JSON object per line, as tools/Eval/score.py documents) and writes one prediction per item in the
// scorer's format. Values are the text as written in the input (the parts' own text), never the standardized text, because the
// gold values are surface text. Nothing is printed except counts.
if (args.Length is not (3 or 4) || args[0] is not ("address" or "name" or "diag"))
{
    Console.Error.WriteLine("usage: eval-rules address|name|diag <gold.jsonl> <predictions.jsonl> [hint]");
    return 64;
}

bool hint = args.Length == 4 && args[3] == "hint";
int n = 0, refused = 0;
using StreamWriter output = new(args[2], false, new UTF8Encoding(false));
foreach (string line in File.ReadLines(args[1]))
{
    if (string.IsNullOrWhiteSpace(line))
    {
        continue;
    }

    if (args[0] == "diag")
    {
        using JsonDocument d = JsonDocument.Parse(line);
        Diag(d.RootElement, output);
        n++;
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

static void Diag(JsonElement root, StreamWriter output)
{
    string text = root.GetProperty("text").GetString()!;
    string gold = root.TryGetProperty("country", out JsonElement c) ? c.GetString()! : string.Empty;
    string[] codes = ["AT", "BE", "HR", "CZ", "DK", "EE", "FI", "FR", "DE", "HU", "IS", "IE", "IT", "LV", "LT", "LU", "MT", "NL", "NO", "PL", "PT", "RO", "SK", "SI", "ES", "SE", "CH", "LI", "AD", "MC", "SM", "VA", "GB", "MX", "AU"];
    List<(string Code, string Key, Dictionary<string, string> Fields)> reads = [];
    foreach (string code in codes)
    {
        if (Extract(text, Region.Parse(code), out ParsedAddress? a, out Dictionary<string, string> f, out _) && a!.Country == Region.Parse(code))
        {
            f.Remove("recipient");
            string key = string.Join('|', f.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + kv.Value.Trim().ToLowerInvariant()));
            reads.Add((code, key, f));
        }
    }

    List<List<(string Code, string Key, Dictionary<string, string> Fields)>> groups = [.. reads.GroupBy(r => r.Key).Select(g => g.ToList())];
    // How the readings differ, as shapes only (digits 9, letters a): the label, whether one value is inside the other, and the shape of the extra words.
    HashSet<string> differing = [];
    for (int i = 1; i < groups.Count; i++)
    {
        foreach (string label in groups[0][0].Fields.Keys.Union(groups[i][0].Fields.Keys))
        {
            groups[0][0].Fields.TryGetValue(label, out string? x);
            groups[i][0].Fields.TryGetValue(label, out string? y);
            x = x?.Trim() ?? string.Empty;
            y = y?.Trim() ?? string.Empty;
            if (string.Equals(x, y, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string rel = x.Length == 0 ? "absent-in-first" : y.Length == 0 ? "absent-in-other" : y.Contains(x, StringComparison.OrdinalIgnoreCase) ? "first-inside-other" : x.Contains(y, StringComparison.OrdinalIgnoreCase) ? "other-inside-first" : "differ";
            string longer = x.Length >= y.Length ? x : y;
            string shorter = x.Length >= y.Length ? y : x;
            string extra = string.Join(' ', longer.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(word => !shorter.Contains(word, StringComparison.OrdinalIgnoreCase)).Select(Shape));
            differing.Add(label + ":" + rel + ":" + extra);
        }
    }

    using MemoryStream ms = new();
    using (Utf8JsonWriter w = new(ms))
    {
        w.WriteStartObject();
        w.WriteString("id", root.GetProperty("id").GetString());
        w.WriteString("gold", gold);
        w.WriteStartArray("reads");
        foreach (var r in reads) { w.WriteStringValue(r.Code); }
        w.WriteEndArray();
        w.WriteStartArray("groups");
        foreach (var g in groups)
        {
            w.WriteStartArray();
            foreach (var r in g) { w.WriteStringValue(r.Code); }
            w.WriteEndArray();
        }

        w.WriteEndArray();
        w.WriteStartArray("differ");
        foreach (string label in differing.Order(StringComparer.Ordinal)) { w.WriteStringValue(label); }
        w.WriteEndArray();
        w.WriteEndObject();
    }

    output.WriteLine(Encoding.UTF8.GetString(ms.ToArray()));
}

static string Shape(string word)
{
    StringBuilder sb = new();
    char last = ' ';
    foreach (char ch in word)
    {
        char k = char.IsDigit(ch) ? '9' : char.IsLetter(ch) ? 'a' : ch;
        if (k != last || k is not ('9' or 'a'))
        {
            sb.Append(k);
        }

        last = k;
    }

    return sb.ToString();
}

static bool WriteAddress(Utf8JsonWriter w, string text, Region region)
{
    bool ok = Extract(text, region, out ParsedAddress? a, out Dictionary<string, string> fields, out Dictionary<string, string> parts);
    // The country the parser settled on (empty when none): lets the outcome tables tell a right country from a wrong one without showing any item.
    w.WriteString("decided_country", ok && a!.Country != Region.Unknown ? a.Country.ToString() : string.Empty);
    WriteMap(w, "fields", fields);
    WriteMap(w, "parts", parts);
    return ok;
}

static bool Extract(string text, Region region, out ParsedAddress? a, out Dictionary<string, string> fields, out Dictionary<string, string> parts)
{
    // The scorer expects blank/absent fields to be omitted.
    fields = [];
    parts = [];
    bool ok = RuleAddressParser.TryParse(text, region, out a, out _) == OperationStatus.Done && a is not null;
    if (ok)
    {
        List<string> road = [], unit = [], box = [];
        foreach (StreetLine line in a!.DeliveryLines)
        {
            for (int pi = 0; pi < line.Parts.Count; pi++)
            {
                StreetPart p = line.Parts[pi];
                switch (p.Role)
                {
                    case StreetPartRole.HouseNumber:
                        // Scoring convention: a building number and a flat number written "12/3" are one house_number in the scorer's labels (the gold sets and the
                        // reference systems write them so); the library keeps the two numbers apart as a house number and a unit number.
                        if (pi + 1 < line.Parts.Count && line.Parts[pi + 1] is { Role: StreetPartRole.UnitNumber } flat && flat.Span.Start > p.Span.End
                            && text.Substring(p.Span.End, flat.Span.Start - p.Span.End).Trim() == "/" && Environment.GetEnvironmentVariable("ONECONTACT_EVAL_NO_SLASH_CONVENTION") is null)
                        {
                            Add(fields, "house_number", text.Substring(p.Span.Start, flat.Span.End - p.Span.Start));
                            pi++;
                            break;
                        }

                        Add(fields, "house_number", p.Text);
                        break;
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
