using System.Diagnostics.Tracing;
using System.Globalization;
using RecursiveAI.OneContact.Addresses.Parsing;
using RecursiveAI.OneContact.Extraction;

namespace RecursiveAI.OneContact.Bench;

/// <summary>
/// Which types a workload allocates, by sampling the runtime's allocation ticks (one event per ~100 KB). Run as
/// <c>profile-alloc address &lt;cases file&gt; &lt;passes&gt;</c> (lines "CC|text") or <c>profile-alloc html|text &lt;rounds&gt;</c>.
/// </summary>
internal static class AllocTypes
{
    private sealed class Listener : EventListener
    {
        internal readonly Dictionary<string, long> Bytes = new(StringComparer.Ordinal);

        protected override void OnEventSourceCreated(EventSource source)
        {
            if (source.Name == "Microsoft-Windows-DotNETRuntime")
            {
                EnableEvents(source, EventLevel.Verbose, (EventKeywords)0x1);
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs e)
        {
            if (e.EventName?.StartsWith("GCAllocationTick", StringComparison.Ordinal) != true || e.Payload is null)
            {
                return;
            }

            string type = "?";
            long amount = 0;
            for (int i = 0; i < e.PayloadNames!.Count; i++)
            {
                if (e.PayloadNames[i] == "TypeName")
                {
                    type = (string)e.Payload[i]!;
                }
                else if (e.PayloadNames[i] == "AllocationAmount64")
                {
                    amount = Convert.ToInt64(e.Payload[i], CultureInfo.InvariantCulture);
                }
            }

            lock (Bytes)
            {
                Bytes[type] = Bytes.GetValueOrDefault(type) + amount;
            }
        }
    }

    public static int Run(string[] args)
    {
        using var listener = new Listener();
        Thread.Sleep(200);
        int n = int.Parse(args[^1], CultureInfo.InvariantCulture);
        if (args[1] == "address")
        {
            List<(Region, string)> cases = [];
            foreach (string line in File.ReadAllLines(args[2]))
            {
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                int bar = line.IndexOf('|', StringComparison.Ordinal);
                string cc = line[..bar];
                cases.Add((cc.Length == 0 ? Region.Unknown : Region.Parse(cc), line[(bar + 1)..].Replace(" // ", "\n", StringComparison.Ordinal)));
            }

            for (int p = 0; p < n; p++)
            {
                foreach ((Region h, string t) in cases)
                {
                    RuleAddressParser.TryParse(t, h, out _, out _);
                }
            }
        }
        else
        {
            Inputs.Page[] pages = Enumerable.Range(1, 8).Select(i => Inputs.MakePage(i, 24)).ToArray();
            var extractor = new ContactExtractor { Sources = ExtractionSources.Markup | ExtractionSources.Text, DefaultRegion = Region.US };
            for (int r = 0; r < n; r++)
            {
                foreach (Inputs.Page p in pages)
                {
                    _ = args[1] == "html" ? extractor.ExtractHtml(p.Html) : extractor.ExtractText(p.Text);
                }
            }
        }

        Thread.Sleep(300);
        long total = listener.Bytes.Values.Sum();
        Console.WriteLine($"sampled {total / 1024} KB");
        foreach ((string type, long bytes) in listener.Bytes.OrderByDescending(k => k.Value).Take(40))
        {
            Console.WriteLine($"{100.0 * bytes / total,5:F1}% {type}");
        }

        return 0;
    }
}
