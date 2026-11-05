using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Nager.PublicSuffix;
using Nager.PublicSuffix.DomainNormalizers;
using Nager.PublicSuffix.RuleProviders;

// usage: oracles-url <directory with urltestdata.json> <public_suffix_list.dat> <test_psl.txt>
// Prints how many entries of the WHATWG URL test data AngleSharp's Url passes, and how many checks of the Public Suffix List's test
// file Nager.PublicSuffix passes. Information only: neither package is read, only run through its public API.
string dir = args[0];

static string? Str(JsonElement e)
{
    if (e.ValueKind == JsonValueKind.Null)
    {
        return null;
    }

    string raw = e.GetRawText();
    var sb = new StringBuilder(raw.Length);
    for (int i = 1; i < raw.Length - 1; i++)
    {
        char c = raw[i];
        if (c != '\\')
        {
            sb.Append(c);
            continue;
        }

        char n = raw[++i];
        switch (n)
        {
            case 'u': sb.Append((char)Convert.ToInt32(raw.Substring(i + 1, 4), 16)); i += 4; break;
            case 'n': sb.Append('\n'); break;
            case 't': sb.Append('\t'); break;
            case 'r': sb.Append('\r'); break;
            case 'b': sb.Append('\b'); break;
            case 'f': sb.Append('\f'); break;
            default: sb.Append(n); break;
        }
    }

    return sb.ToString();
}

using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dir, "urltestdata.json")));
int total = 0;
int pass = 0;
var fieldFails = new Dictionary<string, int>();
foreach (JsonElement e in doc.RootElement.EnumerateArray())
{
    if (e.ValueKind != JsonValueKind.Object)
    {
        continue;
    }

    total++;
    string input = Str(e.GetProperty("input"))!;
    string? baseText = e.TryGetProperty("base", out JsonElement b) ? Str(b) : null;
    bool expectFailure = e.TryGetProperty("failure", out JsonElement f) && f.GetBoolean();
    Url url;
    try
    {
        url = baseText is null ? new Url(input) : new Url(new Url(baseText), input);
    }
    catch (Exception)
    {
        if (expectFailure)
        {
            pass++;
        }

        continue;
    }

    if (expectFailure)
    {
        if (url.IsInvalid)
        {
            pass++;
        }

        continue;
    }

    if (url.IsInvalid)
    {
        continue;
    }

    var actual = new Dictionary<string, string?>
    {
        ["href"] = url.Href,
        ["protocol"] = url.Protocol,
        ["username"] = url.UserName,
        ["password"] = url.Password,
        ["host"] = url.Host,
        ["hostname"] = url.HostName,
        ["port"] = url.Port,
        ["pathname"] = url.PathName,
        ["search"] = url.Search,
        ["hash"] = url.Hash,
        ["origin"] = url.Origin,
    };
    bool all = true;
    foreach ((string key, string? value) in actual)
    {
        if (e.TryGetProperty(key, out JsonElement want) && Str(want) != value)
        {
            all = false;
            fieldFails[key] = fieldFails.GetValueOrDefault(key) + 1;
        }
    }

    if (all)
    {
        pass++;
    }
}

Console.WriteLine($"AngleSharp {typeof(Url).Assembly.GetName().Version} Url on urltestdata.json: total={total} pass={pass} fail={total - pass}; entries failing per field: {string.Join(", ", fieldFails.OrderBy(k => k.Key).Select(k => k.Key + "=" + k.Value))}");

var provider = new LocalFileRuleProvider(args[1]);
await provider.BuildAsync();
var parser = new DomainParser(provider, new IdnMappingDomainNormalizer());
int pslTotal = 0;
int pslPass = 0;
var pslFails = new List<string>();
foreach (string line in File.ReadAllLines(args[2]))
{
    Match m = Regex.Match(line.Trim(), @"^checkPublicSuffix\((null|'[^']*'), (null|'[^']*')\);$");
    if (!m.Success)
    {
        continue;
    }

    pslTotal++;
    string? input = m.Groups[1].Value == "null" ? null : m.Groups[1].Value[1..^1];
    string? want = m.Groups[2].Value == "null" ? null : m.Groups[2].Value[1..^1];
    string? got = null;
    try
    {
        if (input is not null && parser.TryParse(input, out DomainInfo? info) && info is not null)
        {
            got = info.RegistrableDomain;
        }
    }
    catch (Exception)
    {
        got = null;
    }

    if (got == want)
    {
        pslPass++;
    }
    else
    {
        pslFails.Add($"{input ?? "null"}: want {want ?? "null"} got {got ?? "null"}");
    }
}

Console.WriteLine($"Nager.PublicSuffix {typeof(DomainParser).Assembly.GetName().Version} on test_psl.txt: total={pslTotal} pass={pslPass} fail={pslTotal - pslPass}");
foreach (string fail in pslFails.Take(12))
{
    Console.WriteLine("  " + fail);
}
