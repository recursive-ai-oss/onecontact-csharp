using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using NameParser;
using RecursiveAI.OneContact;
using RecursiveAI.OneContact.Addresses.Parsing;
using RecursiveAI.OneContact.Models;
using RecursiveAI.OneContact.Models.Tier1;
using RecursiveAI.OneContact.Names;

// usage: eval-speed <model.onenlp> <address dev jsonl> <names dev jsonl> [count]
// Times parsing of the first <count> texts of each file with rules only, with rules and the Tier 1 model, and with the comparators; prints
// items per second, allocated bytes per item, the model's load time and the process's peak working set.
string modelPath = args[0];
int count = args.Length > 3 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 5000;
List<string> addresses = Read(args[1], count);
List<string> names = Read(args[2], count);

var load = Stopwatch.StartNew();
Tier1Model model;
using (FileStream stream = File.OpenRead(modelPath))
{
    model = Tier1Model.Load(stream);
}

model.Tag(addresses[0]);
model.Analyze(names[0]);
load.Stop();
Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"model load + first call: {load.Elapsed.TotalMilliseconds:F0} ms"));

Time("address, rules only", addresses, t => RuleAddressParser.TryParse(t, Region.Unknown, out _, out _));
USAddress.AddressParser theirs = new();
Time("address, AddressParser 1.0.3 (US regex)", addresses, t => theirs.ParseAddress(t.Replace("\n", ", ", StringComparison.Ordinal)));
Time("name, rules only", names, t => NameParsing.Split(t));
Time("name, NameParserSharp 1.5.0", names, t => _ = new HumanName(t));
ContactModels.Register(Tier1Model.PackageId, 1, model, model);
Time("address, rules + model", addresses, t => RuleAddressParser.TryParse(t, Region.Unknown, out _, out _));
Time("name, rules + model", names, t => NameParsing.Split(t));
Time("model.Tag alone", addresses, t => model.Tag(t));
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
