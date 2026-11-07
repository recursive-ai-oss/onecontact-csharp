using System.Buffers;
using System.Diagnostics;
using RecursiveAI.OneContact;
using RecursiveAI.OneContact.Addresses.Parsing;

// Reads lines "CC|text" (CC may be empty for no hint; " // " in the text is a line break) and prints the fields the rules find, or the refusal reason.
// With "--time N" it only measures: N passes over all cases after a warm-up pass, and prints the mean microseconds per parse (indicative; the build is the one being measured).
List<(Region Hint, string Text, string Line)> cases = [];
string? line;
while ((line = Console.In.ReadLine()) is not null)
{
    if (line.Length == 0 || line[0] == '#')
    {
        continue;
    }

    int bar = line.IndexOf('|', StringComparison.Ordinal);
    string cc = line[..bar];
    cases.Add((cc.Length == 0 ? Region.Unknown : Region.Parse(cc), line[(bar + 1)..].Replace(" // ", "\n", StringComparison.Ordinal), line));
}

if (args.Length == 2 && args[0] == "--time")
{
    int passes = int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);
    foreach ((Region h, string t, string _) in cases)
    {
        RuleAddressParser.TryParse(t, h, out _, out _);
    }

    Stopwatch sw = Stopwatch.StartNew();
    for (int p = 0; p < passes; p++)
    {
        foreach ((Region h, string t, string _) in cases)
        {
            RuleAddressParser.TryParse(t, h, out _, out _);
        }
    }

    sw.Stop();
    Console.WriteLine($"{cases.Count} cases x {passes} passes: {sw.Elapsed.TotalMilliseconds * 1000 / (passes * cases.Count):F1} us per parse; GC gen0 {GC.CollectionCount(0)}; allocated {GC.GetTotalAllocatedBytes() / (passes * cases.Count + cases.Count)} bytes per parse");
    return;
}

foreach ((Region hint, string text, string original) in cases)
{
    OperationStatus s = RuleAddressParser.TryParse(text, hint, out ParsedAddress? a, out ParseDetail d);
    if (s != OperationStatus.Done || a is null)
    {
        Console.WriteLine($"{original}\n   REFUSED {d.PrimaryReason}");
        continue;
    }

    Console.WriteLine($"{original}\n   {a.Country}: " + string.Join("; ", a.LeadingLines.Select(l => "lead=" + l.Text))
        + "; " + string.Join("; ", a.DeliveryLines.Select(l => "del=" + string.Join("+", l.Parts.Select(p => p.Role.ToString()[..3] + ":" + p.Text)))) + $"; loc={a.Locality?.Text}; adm={a.AdminArea?.Text}; pc={a.PostalCode?.Text}; cn={a.CountryName?.Text}; facts={a.Facts}; issues={string.Join("+", d.Issues.Select(i => i.Reason.ToString()))}");
}
