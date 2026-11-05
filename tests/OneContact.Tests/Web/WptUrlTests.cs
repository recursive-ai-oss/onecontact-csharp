using System.Text;
using System.Text.Json;
using RecursiveAI.OneContact.Text.Idna;
using RecursiveAI.OneContact.Web;

namespace RecursiveAI.OneContact.Tests;

/// <summary>
/// The WHATWG URL conformance data from web-platform-tests (testdata/wpt, BSD-3-Clause, pinned in upstream/wpt-url/PIN) run against the
/// URL parser, its setters and the domain-to-ASCII step. Every entry is checked; a failure lists the entry.
/// </summary>
public class WptUrlTests
{
    private static string DataPath(string name) => Path.Combine(AppContext.BaseDirectory, "testdata", "wpt", name);

    // The data holds lone surrogates written as \uXXXX escapes, which JsonElement.GetString refuses; unescape by hand instead.
    internal static string? Str(JsonElement e)
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
                case 'u':
                    sb.Append((char)Convert.ToInt32(raw.Substring(i + 1, 4), 16));
                    i += 4;
                    break;
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

    private static void Report(string name, int total, List<string> failures)
    {
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "wpt-" + name + ".txt"), $"{name}: total={total} pass={total - failures.Count} fail={failures.Count}\n" + string.Join('\n', failures));
        Assert.True(failures.Count == 0, $"{name}: {failures.Count} of {total} failed:\n" + string.Join('\n', failures.Take(30)));
    }

    [Fact]
    public void UrlTestData()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(DataPath("urltestdata.json")));
        var failures = new List<string>();
        int total = 0;
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

            UrlRecord? baseUrl = null;
            bool baseOk = true;
            if (baseText is not null)
            {
                baseOk = UrlParser.TryParse(baseText, null, out UrlRecord parsedBase, out _);
                baseUrl = parsedBase;
            }

            UrlRecord url = new();
            bool ok = baseOk && UrlParser.TryParse(input, baseUrl, out url, out _);

            if (expectFailure)
            {
                if (ok)
                {
                    failures.Add($"expected failure: {Show(input)} base {Show(baseText)} gave {url.Serialize()}");
                }

                continue;
            }

            if (!ok)
            {
                failures.Add($"unexpected failure: {Show(input)} base {Show(baseText)}");
                continue;
            }

            var actual = new Dictionary<string, string>
            {
                ["href"] = url.Serialize(),
                ["protocol"] = url.ProtocolText,
                ["username"] = url.Username,
                ["password"] = url.Password,
                ["host"] = url.HostText,
                ["hostname"] = url.HostnameText,
                ["port"] = url.PortText,
                ["pathname"] = url.SerializePath(),
                ["search"] = url.SearchText,
                ["hash"] = url.HashText,
                ["origin"] = url.OriginText,
            };
            foreach ((string key, string value) in actual)
            {
                if (e.TryGetProperty(key, out JsonElement want) && Str(want) != value)
                {
                    failures.Add($"{key}: {Show(input)} base {Show(baseText)}: want {Show(Str(want))} got {Show(value)}");
                }
            }
        }

        Report("urltestdata", total, failures);
    }

    [Fact]
    public void SettersTests()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(DataPath("setters_tests.json")));
        var failures = new List<string>();
        int total = 0;
        foreach (JsonProperty group in doc.RootElement.EnumerateObject())
        {
            if (group.Value.ValueKind != JsonValueKind.Array || group.Name == "comment")
            {
                continue;
            }

            foreach (JsonElement t in group.Value.EnumerateArray())
            {
                total++;
                string href = Str(t.GetProperty("href"))!;
                string value = Str(t.GetProperty("new_value"))!;
                UrlParser.TryParse(href, null, out UrlRecord url, out _);
                if (group.Name == "href")
                {
                    bool parsed = UrlParser.TryParse(value, null, out UrlRecord replaced, out _);
                    if (t.GetProperty("expected").TryGetProperty("exception", out _))
                    {
                        if (parsed)
                        {
                            failures.Add($"href setter {Show(value)}: expected an exception");
                        }

                        continue;
                    }

                    if (parsed)
                    {
                        url = replaced;
                    }
                }
                else
                {
                    switch (group.Name)
                    {
                        case "protocol": UrlSetters.SetProtocol(url, value); break;
                        case "username": UrlSetters.SetUsername(url, value); break;
                        case "password": UrlSetters.SetPassword(url, value); break;
                        case "host": UrlSetters.SetHost(url, value); break;
                        case "hostname": UrlSetters.SetHostname(url, value); break;
                        case "port": UrlSetters.SetPort(url, value); break;
                        case "pathname": UrlSetters.SetPathname(url, value); break;
                        case "search": UrlSetters.SetSearch(url, value); break;
                        case "hash": UrlSetters.SetHash(url, value); break;
                        default: failures.Add("unknown setter " + group.Name); break;
                    }
                }

                var actual = new Dictionary<string, string>
                {
                    ["href"] = url.Serialize(),
                    ["protocol"] = url.ProtocolText,
                    ["username"] = url.Username,
                    ["password"] = url.Password,
                    ["host"] = url.HostText,
                    ["hostname"] = url.HostnameText,
                    ["port"] = url.PortText,
                    ["pathname"] = url.SerializePath(),
                    ["search"] = url.SearchText,
                    ["hash"] = url.HashText,
                };
                foreach (JsonProperty want in t.GetProperty("expected").EnumerateObject())
                {
                    if (actual.TryGetValue(want.Name, out string? got) && Str(want.Value) != got)
                    {
                        failures.Add($"{group.Name} {Show(href)} <- {Show(value)}: {want.Name} want {Show(Str(want.Value))} got {Show(got)}");
                    }
                }
            }
        }

        Report("setters_tests", total, failures);
    }

    [Fact]
    public void ToAsciiData()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(DataPath("toascii.json")));
        var failures = new List<string>();
        int total = 0;
        foreach (JsonElement e in doc.RootElement.EnumerateArray())
        {
            if (e.ValueKind == JsonValueKind.Object)
            {
                total++;
                CheckHost(e, failures);
            }
        }

        Report("toascii", total, failures);
    }

    [Fact]
    public void IdnaTestV2Data()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(DataPath("IdnaTestV2.json")));
        var failures = new List<string>();
        int total = 0;
        foreach (JsonElement e in doc.RootElement.EnumerateArray())
        {
            if (e.ValueKind == JsonValueKind.Object)
            {
                total++;
                CheckHost(e, failures);
            }
        }

        Report("IdnaTestV2", total, failures);
    }

    // The way web-platform-tests run these files: the input is the host of "https://<input>/".
    private static void CheckHost(JsonElement e, List<string> failures)
    {
        string input = Str(e.GetProperty("input"))!;
        string? output = Str(e.GetProperty("output"));
        if (input.Length == 0)
        {
            // An empty host cannot be written in a URL; the entry is about ToASCII itself.
            Assert.True(Uts46.TryToAscii(input, Uts46Options.CheckBidi | Uts46Options.CheckJoiners, out string empty) && empty == output);
            return;
        }

        bool ok = UrlParser.TryParse("https://" + input + "/x", null, out UrlRecord url, out _);
        if (output is null)
        {
            if (ok)
            {
                failures.Add($"expected failure: {Show(input)} gave {url.HostnameText}");
            }
        }
        else if (!ok)
        {
            failures.Add($"unexpected failure: {Show(input)} want {Show(output)}");
        }
        else if (url.HostnameText != output)
        {
            failures.Add($"{Show(input)}: want {Show(output)} got {Show(url.HostnameText)}");
        }
    }

    internal static string Show(string? s)
    {
        if (s is null)
        {
            return "null";
        }

        var sb = new StringBuilder("\"");
        foreach (char c in s)
        {
            if (c is < ' ' or > '~')
            {
                sb.Append("\\u").Append(((int)c).ToString("x4", System.Globalization.CultureInfo.InvariantCulture));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.Append('"').ToString();
    }

    [Fact]
    public void Punycode_RoundTripsRfc3492Samples()
    {
        // RFC 3492 section 7.1, sample (A) Arabic (Egyptian) and (L) 3<nen>B<gumi><kinpachi><sensei>.
        Assert.True(Punycode.TryEncode([0x0644, 0x064A, 0x0647, 0x0645, 0x0627, 0x0628, 0x062A, 0x0643, 0x0644, 0x0645, 0x0648, 0x0634, 0x0639, 0x0631, 0x0628, 0x064A, 0x061F], out string a));
        Assert.Equal("egbpdaj6bu4bxfgehfvwxn", a);
        Assert.True(Punycode.TryEncode([0x0033, 0x5E74, 0x0042, 0x7D44, 0x91D1, 0x516B, 0x5148, 0x751F], out string l));
        Assert.Equal("3B-ww4c5e180e575a65lsy2b", l);
        Assert.True(Punycode.TryDecode("egbpdaj6bu4bxfgehfvwxn", out int[] back));
        Assert.Equal(17, back.Length);
    }
}
