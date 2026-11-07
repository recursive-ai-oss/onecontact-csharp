namespace RecursiveAI.OneContact.Fuzz;

/// <summary>
/// Usage: <c>fuzz &lt;target&gt; &lt;file|directory|-&gt;</c>. Each file is fed to the target as bytes (text targets decode them as
/// UTF-8, so bad bytes become U+FFFD); <c>-</c> reads one input from standard input. A target must never throw: the parsers report bad input through their
/// status, and <c>Parse</c> may throw only <see cref="ContactParseException"/>. Anything else is a finding, and the process exits 1.
/// <c>afl &lt;target&gt;</c> runs the target under AFL++ with SharpFuzz instrumentation (see <c>fuzz/README.md</c>).
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 4 && args[2] == "--seconds" && Targets.All.TryGetValue(args[0], out Action<byte[]>? timed) && int.TryParse(args[3], out int seconds))
        {
            return Mutate(args[0], timed, args[1], seconds);
        }

        // Coverage-guided mode: the instrumented libraries report to AFL++, which feeds each input on standard input.
        if (args.Length == 2 && args[0] == "afl" && Targets.All.TryGetValue(args[1], out Action<byte[]>? guided))
        {
            SharpFuzz.Fuzzer.Run(stream =>
            {
                using MemoryStream copy = new();
                stream.CopyTo(copy);
                guided(copy.ToArray());
            });
            return 0;
        }

        if (args.Length == 3 && args[0] == "address-smoke")
        {
            return AddressSmoke.Run(args[1], int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture));
        }

        if (args.Length == 3 && args[0] == "names-smoke")
        {
            return NameSmoke.Run(args[1], int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture));
        }

        if (args.Length != 2 || !Targets.All.TryGetValue(args[0], out Action<byte[]>? target))
        {
            Console.Error.WriteLine("usage: fuzz <" + string.Join('|', Targets.All.Keys) + "> <file|directory|->");
            return 64;
        }

        if (args[1] == "-")
        {
            using Stream stdin = Console.OpenStandardInput();
            using MemoryStream copy = new();
            stdin.CopyTo(copy);
            target(copy.ToArray());
            return 0;
        }

        IEnumerable<string> files = Directory.Exists(args[1]) ? Directory.EnumerateFiles(args[1]) : [args[1]];
        int count = 0;
        foreach (string file in files)
        {
            target(File.ReadAllBytes(file));
            count++;
        }

        Console.WriteLine($"{args[0]}: {count} input(s), no findings");
        return 0;
    }

    // A small in-process mutation loop: no coverage feedback, a fixed seed, byte flips, splices and truncations of the seed files.
    private static int Mutate(string name, Action<byte[]> target, string seedDirectory, int seconds)
    {
        List<byte[]> seeds = [.. Directory.EnumerateFiles(seedDirectory).Order(StringComparer.Ordinal).Select(File.ReadAllBytes)];
        Random random = new(12345);
        System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        long executions = 0;
        while (clock.Elapsed.TotalSeconds < seconds)
        {
            byte[] input = [.. seeds[random.Next(seeds.Count)]];
            int edits = 1 + random.Next(4);
            for (int i = 0; i < edits && input.Length > 0; i++)
            {
                switch (random.Next(4))
                {
                    case 0:
                        input[random.Next(input.Length)] = (byte)random.Next(256);
                        break;
                    case 1:
                        input = input[..random.Next(input.Length + 1)];
                        break;
                    case 2:
                        byte[] other = seeds[random.Next(seeds.Count)];
                        int cut = random.Next(input.Length);
                        input = [.. input[..cut], .. other[random.Next(other.Length)..]];
                        break;
                    default:
                        int at = random.Next(input.Length);
                        input = [.. input[..at], .. input[at..].Take(random.Next(1, 40)), .. input[at..]];
                        break;
                }
            }

            try
            {
                target(input);
            }
            catch (Exception e)
            {
                string repro = Path.Combine(Path.GetTempPath(), $"{name}-finding-{executions}.bin");
                File.WriteAllBytes(repro, input);
                Console.Error.WriteLine($"{name}: finding {e.GetType().Name} after {executions} executions; input saved to {repro}");
                return 1;
            }

            executions++;
        }

        Console.WriteLine($"{name}: {executions} executions in {seconds} s, no findings");
        return 0;
    }
}
