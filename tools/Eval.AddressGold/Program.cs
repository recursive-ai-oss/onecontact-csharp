using System.Buffers;
using System.Text;
using RecursiveAI.OneContact;
using RecursiveAI.OneContact.Addresses.Parsing;
using RecursiveAI.OneContact.Addresses.Standardization;
using RecursiveAI.OneContact.Addresses.Streets;

// usage: Eval.AddressGold <parse-gold.tsv> [--misses]
//        Eval.AddressGold --key <text>...      prints the version 1 key and the postal and display renderings of each address
//        Eval.AddressGold --sample <file>      writes the display-casing sample (a markdown table of varied addresses rendered both ways)
// Field accuracy = fields right / gold fields; full-parse accuracy = every gold field right and no extra field in the keys the gold leaves empty.
if (args.Length > 0 && args[0] == "--key")
{
    foreach (string a in args.Skip(1))
    {
        PostalAddress p = PostalAddress.Parse(a.Replace("\\n", "\n", StringComparison.Ordinal));
        Console.WriteLine(a.Replace("\n", "\\n", StringComparison.Ordinal) + "\t" + p.Key);
    }

    return 0;
}

if (args.Length > 0 && args[0] == "--explain")
{
    // Prints how the rules read each text (\n in an argument is a line break): the status, the reason when refused, and every field with its span and rule.
    foreach (string a in args.Skip(1))
    {
        string text = a.Replace("\\n", "\n", StringComparison.Ordinal);
        OperationStatus st = RuleAddressParser.TryParse(text, Region.Unknown, out ParsedAddress? pa, out ParseDetail pd);
        Console.WriteLine(a + "  =>  " + (st == OperationStatus.Done ? "read" : "refused " + pd.PrimaryReason.Id));
        if (pa is not null)
        {
            foreach (ParsedAddressField f in pa.Fields)
            {
                Console.WriteLine($"    {f.Name} [{f.Span.Start},{f.Span.End}) '{f.Text}' -> '{f.Standard}' ({f.Rule})");
            }
        }
    }

    return 0;
}

if (args.Length > 1 && args[0] == "--sample")
{
    StringBuilder md = new();
    md.AppendLine("# Postal and display renderings, United States and Canada");
    md.AppendLine();
    md.AppendLine("Each input is read by the rules and written four ways: `Postal` is the authority's style (upper case, its abbreviations); `Display` is the same tokens in natural title case. Label = one line per address line; single line = lines joined by a comma. Only the tokens of the input are rewritten; nothing is added.");
    md.AppendLine();
    int n = 0;
    foreach (string input in Sample.Inputs)
    {
        n++;
        if (PostalAddress.TryParse(input, out PostalAddress pa, out ParseDetail pd) != OperationStatus.Done)
        {
            md.AppendLine($"## {n}. (not read: {pd.PrimaryReason.Id})");
            md.AppendLine();
            continue;
        }

        md.AppendLine($"## {n}. `{input.Replace("\n", " / ", StringComparison.Ordinal)}`");
        md.AppendLine();
        md.AppendLine("| | Label | Single line |");
        md.AppendLine("|---|---|---|");
        md.AppendLine($"| Postal | `{pa.ToString(AddressStyle.Postal).Replace("\n", "` <br> `", StringComparison.Ordinal)}` | `{pa.ToString(AddressStyle.Postal, AddressLayout.SingleLine)}` |");
        md.AppendLine($"| Display | {pa.ToString(AddressStyle.Display).Replace("\n", " <br> ", StringComparison.Ordinal)} | {pa.ToString(AddressStyle.Display, AddressLayout.SingleLine)} |");
        md.AppendLine();
    }

    File.WriteAllText(args[1], md.ToString());
    return 0;
}

string[] keys = ["house_number", "pre_directional", "street_name", "street_type", "post_directional", "unit", "box", "locality", "state", "postal"];
bool showMisses = args.Contains("--misses");
List<(string Id, string Country, string Text, Dictionary<string, string> Gold)> gold = [];
foreach (string line in File.ReadLines(args[0]))
{
    if (line.Length == 0 || line[0] == '#')
    {
        continue;
    }

    string[] c = line.Split('\t');
    Dictionary<string, string> g = c[3].Split(';', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => p[1]);
    gold.Add((c[0], c[1], c[2].Replace("\\n", "\n", StringComparison.Ordinal), g));
}

USAddress.AddressParser theirs = new();
Score ours = new(), oursUs = new(), oursCa = new(), theirsUs = new();
int unread = 0;
foreach ((string id, string country, string text, Dictionary<string, string> g) in gold)
{
    Dictionary<string, string> o = Ours(text);
    if (o.Count == 0)
    {
        unread++;
    }

    ours.Add(g, o, keys);
    (country == "US" ? oursUs : oursCa).Add(g, o, keys);
    if (showMisses && !Same(g, o, keys))
    {
        Console.WriteLine($"MISS {id}: {text.Replace("\n", " / ", StringComparison.Ordinal)}");
        foreach (string k in keys)
        {
            string gv = g.GetValueOrDefault(k, string.Empty);
            string ov = o.GetValueOrDefault(k, string.Empty);
            if (gv != ov)
            {
                Console.WriteLine($"    {k}: gold '{gv}' ours '{ov}'");
            }
        }
    }

    if (country == "US")
    {
        theirsUs.Add(g, Theirs(text), keys);
    }
}

Console.WriteLine($"gold addresses: {gold.Count} (US {gold.Count(x => x.Country == "US")}, CA {gold.Count(x => x.Country == "CA")}); our parse refused: {unread}");
Console.WriteLine("| set | parser | field accuracy | full-parse accuracy |");
Console.WriteLine("|---|---|---|---|");
Console.WriteLine($"| US + CA | OneContact rules | {ours.Field:P1} | {ours.Full:P1} |");
Console.WriteLine($"| US | OneContact rules | {oursUs.Field:P1} | {oursUs.Full:P1} |");
Console.WriteLine($"| US | AddressParser 1.0.3 | {theirsUs.Field:P1} | {theirsUs.Full:P1} |");
Console.WriteLine($"| CA | OneContact rules | {oursCa.Field:P1} | {oursCa.Full:P1} |");
Console.WriteLine("per field (US, ours / AddressParser): " + string.Join(", ", keys.Select(k => $"{k} {oursUs.ByKey(k):P0}/{theirsUs.ByKey(k):P0}")));
return 0;

static bool Same(Dictionary<string, string> g, Dictionary<string, string> o, string[] keys) =>
    keys.All(k => string.Equals(g.GetValueOrDefault(k, string.Empty), o.GetValueOrDefault(k, string.Empty), StringComparison.Ordinal));

static Dictionary<string, string> Ours(string text)
{
    Dictionary<string, string> r = [];
    if (RuleAddressParser.TryParse(text, Region.Unknown, out ParsedAddress? a, out _) != OperationStatus.Done || a is null)
    {
        return r;
    }

    void Put(string k, string v)
    {
        if (v.Length > 0 && !r.ContainsKey(k))
        {
            r[k] = v;
        }
    }

    foreach (StreetLine line in a.DeliveryLines)
    {
        if (line.Kind is StreetLineKind.PostOfficeBox or StreetLineKind.RuralRoute or StreetLineKind.HighwayContractRoute or StreetLineKind.GeneralDelivery or StreetLineKind.Military)
        {
            Put("box", line.ToPostalString());
            continue;
        }

        List<string> name = [];
        foreach (StreetPart p in line.Parts)
        {
            switch (p.Role)
            {
                case StreetPartRole.HouseNumber: Put("house_number", p.Standard); break;
                case StreetPartRole.PreDirectional: Put("pre_directional", p.Standard); break;
                case StreetPartRole.StreetName: name.Add(p.Standard); break;
                case StreetPartRole.StreetType: Put("street_type", p.Standard); break;
                case StreetPartRole.PostDirectional: Put("post_directional", p.Standard); break;
                case StreetPartRole.UnitPrefix: Put("unit", "UNIT " + p.Standard.TrimEnd('-')); break;
            }
        }

        Put("street_name", string.Join(' ', name));
        StreetPart? d = line.Find(StreetPartRole.UnitDesignator);
        StreetPart? n = line.Find(StreetPartRole.UnitNumber);
        if (d is not null && n is not null)
        {
            Put("unit", d.Value.Standard + " " + n.Value.Standard);
        }
    }

    Put("locality", a.Locality?.Standard ?? string.Empty);
    Put("state", a.AdminArea?.Standard ?? string.Empty);
    Put("postal", a.PostalCode?.Standard ?? string.Empty);
    return r;
}

Dictionary<string, string> Theirs(string text)
{
    Dictionary<string, string> r = [];
    USAddress.AddressParseResult? p;
    try
    {
        p = theirs.ParseAddress(text.Replace("\n", ", ", StringComparison.Ordinal));
    }
    catch (Exception)
    {
        return r;
    }

    if (p is null)
    {
        return r;
    }

    void Put(string k, string? v)
    {
        if (!string.IsNullOrWhiteSpace(v))
        {
            r[k] = v.Trim().ToUpperInvariant();
        }
    }

    Put("house_number", p.Number);
    Put("pre_directional", p.Predirectional);
    Put("street_name", p.Street);
    Put("street_type", p.Suffix);
    Put("post_directional", p.Postdirectional);
    if (!string.IsNullOrWhiteSpace(p.SecondaryUnit) || !string.IsNullOrWhiteSpace(p.SecondaryNumber))
    {
        Put("unit", ((p.SecondaryUnit ?? string.Empty) + " " + (p.SecondaryNumber ?? string.Empty)).Trim());
    }

    Put("locality", p.City);
    Put("state", p.State);
    Put("postal", p.Zip);
    return r;
}

internal sealed class Score
{
    private int _fields;
    private int _right;
    private int _full;
    private int _items;
    private readonly Dictionary<string, (int Right, int Total)> _by = [];

    internal double Field => _fields == 0 ? 0 : (double)_right / _fields;

    internal double Full => _items == 0 ? 0 : (double)_full / _items;

    internal double ByKey(string k) => _by.TryGetValue(k, out (int Right, int Total) v) && v.Total > 0 ? (double)v.Right / v.Total : 0;

    internal void Add(Dictionary<string, string> gold, Dictionary<string, string> got, string[] keys)
    {
        _items++;
        bool all = true;
        foreach (string k in keys)
        {
            string gv = gold.GetValueOrDefault(k, string.Empty);
            string ov = got.GetValueOrDefault(k, string.Empty);
            // The postal code of a comparator that returns only five digits is compared on five digits.
            if (k == "postal" && ov.Length == 5 && gv.Length > 5)
            {
                gv = gv[..5];
            }

            if (gv.Length > 0)
            {
                _fields++;
                (int r, int t) = _by.GetValueOrDefault(k);
                _by[k] = (r + (gv == ov ? 1 : 0), t + 1);
                if (gv == ov)
                {
                    _right++;
                }
            }

            if (gv != ov)
            {
                all = false;
            }
        }

        if (all)
        {
            _full++;
        }
    }
}

internal static class Sample
{
    internal static readonly string[] Inputs =
    [
        "1600 pennsylvania ave nw, washington, dc 20500",
        "350 FIFTH AVENUE\nNEW YORK, NEW YORK 10118",
        "742 Evergreen Terrace\nSpringfield, OR 97477",
        "425 flower blvd #72, los angeles ca 90012",
        "55 Sylvan Boulevard Room 108\nFalls Church VA 22042-1441",
        "101 W MAIN ST S APT 101, WASHINGTON DC 20019-4649",
        "112-10 BRONX RD, QUEENS NY 11375",
        "123 1/2 main st, dover de 19901",
        "N6W23001 Bluemound Rd, Waukesha WI 53186",
        "39.2 Rd, Salt Lake City UT 84101",
        "842 E 1700 S, Salt Lake City, UT 84105",
        "PO Box 11890\nSan Diego CA 92101",
        "P.O. Box -0145, Tampa FL 33630",
        "RR 4 Box 87A, Dover OH 44622",
        "RFD Route 4 #87A, Dover OH 44622",
        "HC 68 Box 23A, Fairview TX 75069",
        "General Delivery, Tampa FL 33602-9999",
        "MR JOHN DOE\nPSC 3022 BOX 684\nFPO AE 09397-0007",
        "Unit 9900 Box 0500, DPO AE 09701-0500",
        "SSGT KEVIN TAYLOR\nUPR 10150 BOX 4190\nAPO AP 96278-5041",
        "1234 calle aurora, mayaguez, pr 00680",
        "URB Las Gladiolas\n150 Calle A\nSan Juan PR 00926-0221",
        "CALLE 1 A17\nPONCE PR 00731",
        "1234 Ave Ashford Apt 1A, San Juan, Puerto Rico 00907",
        "400 Vagliowaway Way, Frederiksted VI 00850",
        "10-123 1/2 main st se, montreal qc h3z 2y7",
        "123 MAIN ST APT 10\nMONTREAL QC  H3Z 2Y7",
        "10-123 RUE MAIN\nMONTRÉAL QC H3Z 2Y7",
        "45 Boul. René-Lévesque Ouest, Québec, QC G1R 5R7",
        "10-123 1RE RUE\nMONTREAL (QUEBEC) H3Z 2Y7",
        "PO BOX 4001 STN A\nVICTORIA BC V8X 3X4",
        "CP 4001 SUCC A, Victoria BC V8X 3X4",
        "RR 4 LCD MAIN\nLLOYDMINSTER AB T9V 2Z9",
        "SITE 6 COMP 10\nRR 6 STN MAIN\nVICTORIA BC V8X 3X4",
        "GD STN A, CALGARY, ALBERTA T0H 1A0",
        "Mr. Walter W. Witherspoon Jr.\nMDM Enterprises, Inc.\n1401 S. Main St.\nPlummer's Landing, KY 41081-1411",
    ];
}
