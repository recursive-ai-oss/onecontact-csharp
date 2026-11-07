using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using NameParser;
using RecursiveAI.OneContact;
using RecursiveAI.OneContact.Addresses.Parsing;
using RecursiveAI.OneContact.Models;
using RecursiveAI.OneContact.Models.Superset;
using RecursiveAI.OneContact.Models.Tier1;
using RecursiveAI.OneContact.Names;

// usage: eval-speed <model.onenlp> <address dev jsonl> <names dev jsonl> [count]
// With the environment variable SUPERSET=1 the file is the Tier 1 + Tier 2 model (registered at rank 2); otherwise it is the Tier 1 model.
// Prints the SHA-256 of what the model says about every text (labels, offsets and the bits of every confidence), so two builds can be compared byte for byte.
// Times parsing of the first <count> texts of each file with rules only, with rules and the Tier 1 model, and with the comparators; prints
// items per second, allocated bytes per item, the model's load time and the process's peak working set.
string modelPath = args[0];
int count = args.Length > 3 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 5000;
List<string> addresses = Read(args[1], count);
List<string> names = Read(args[2], count);

var load = Stopwatch.StartNew();
bool superset = Environment.GetEnvironmentVariable("SUPERSET") == "1";
IAddressFieldModel addressModel;
INameModel nameModel;
string packageId;
int rank;
using (FileStream stream = File.OpenRead(modelPath))
{
    if (superset)
    {
        SupersetModel m = SupersetModel.Load(stream);
        (addressModel, nameModel, packageId, rank) = (m, m, SupersetModel.PackageId, 2);
    }
    else
    {
        Tier1Model m = Tier1Model.Load(stream);
        (addressModel, nameModel, packageId, rank) = (m, m, Tier1Model.PackageId, 1);
    }
}

addressModel.Tag(addresses[0]);
nameModel.Analyze(names[0]);
load.Stop();
Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"model load + first call: {load.Elapsed.TotalMilliseconds:F0} ms"));
Console.WriteLine("outputs sha256: " + OutputsHash(addressModel, nameModel, addresses, names));

Time("address, rules only", addresses, t => RuleAddressParser.TryParse(t, Region.Unknown, out _, out _));
USAddress.AddressParser theirs = new();
Time("address, AddressParser 1.0.3 (US regex)", addresses, t => theirs.ParseAddress(t.Replace("\n", ", ", StringComparison.Ordinal)));
Time("name, rules only", names, t => NameParsing.Split(t));
Time("name, NameParserSharp 1.5.0", names, t => _ = new HumanName(t));
ContactModels.Register(packageId, rank, addressModel, nameModel);
Time("address, rules + model", addresses, t => RuleAddressParser.TryParse(t, Region.Unknown, out _, out _));
Time("name, rules + model", names, t => NameParsing.Split(t));
Time("model.Tag alone", addresses, t => addressModel.Tag(t));
Time("model.Analyze alone", names, t => nameModel.Analyze(t));
Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"peak working set: {Process.GetCurrentProcess().PeakWorkingSet64 / 1048576.0:F0} MB"));

static void Time(string label, List<string> items, Action<string> work)
{
    foreach (string t in items.Take(200))
    {
        try { work(t); } catch (Exception) { }
    }

    long bytes0 = GC.GetAllocatedBytesForCurrentThread();
    var sw = Stopwatch.StartNew();
    foreach (string t in items)
    {
        try { work(t); } catch (Exception) { }
    }

    sw.Stop();
    long bytes = GC.GetAllocatedBytesForCurrentThread() - bytes0;
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{label,-40} {items.Count / sw.Elapsed.TotalSeconds,10:F0} items/s  {bytes / items.Count,10:F0} B/item"));
}

static string OutputsHash(IAddressFieldModel addressModel, INameModel nameModel, List<string> addresses, List<string> names)
{
    using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    void Add(string s) => hash.AppendData(System.Text.Encoding.UTF8.GetBytes(s + "\u0001"));
    void Span(ModelSpan sp) => Add($"{sp.Start},{sp.Length},{sp.Label},{BitConverter.SingleToInt32Bits(sp.Confidence)}");
    foreach (string t in addresses)
    {
        Add("A" + t);
        foreach (ModelSpan sp in addressModel.Tag(t)) { Span(sp); }
    }

    string addressHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    foreach (string t in names)
    {
        Add("N" + t);
        NameModelResult r = nameModel.Analyze(t);
        Add($"{r.Class},{BitConverter.SingleToInt32Bits(r.ClassConfidence)}");
        foreach (ModelSpan sp in r.Parts) { Span(sp); }
    }

    return "addresses " + addressHash + " names " + Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
}

static List<string> Read(string path, int count)
{
    List<string> result = [];
    foreach (string line in File.ReadLines(path))
    {
        using JsonDocument doc = JsonDocument.Parse(line);
        result.Add(doc.RootElement.GetProperty("text").GetString()!);
        if (result.Count == count)
        {
            break;
        }
    }

    return result;
}
