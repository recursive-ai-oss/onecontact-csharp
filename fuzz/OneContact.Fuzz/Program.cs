using System.Text;
using RecursiveAI.OneContact.Phones;

namespace RecursiveAI.OneContact.Fuzz;

/// <summary>
/// Usage: <c>fuzz &lt;target&gt; &lt;file|directory|-&gt;</c>. Each file is decoded as UTF-8 (bad bytes become U+FFFD) and fed to the
/// target; <c>-</c> reads one input from standard input. A target must never throw: the parsers report bad input through their
/// status, and <c>Parse</c> may throw only <see cref="ContactParseException"/>. Anything else is a finding, and the process exits 1.
/// </summary>
internal static class Program
{
    private static readonly Dictionary<string, Action<string>> Targets = new(StringComparer.Ordinal)
    {
        ["phone"] = text => { _ = PhoneNumber.TryParse(text, Region.US, out _, out _); _ = PhoneNumber.TryParse(text, Region.Unknown, out _, out _); Expect(() => PhoneNumber.Parse(text, Region.US)); },
        ["phone-metadata"] = LoadMetadata,
        ["phone-match"] = MatchAll,
        ["phone-typing"] = TypeAll,
        ["email"] = text => { _ = EmailAddress.TryParse(text, out _, out _); Expect(() => EmailAddress.Parse(text)); },
        ["web"] = text => { _ = WebAddress.TryParse(text, out _, out _); Expect(() => WebAddress.Parse(text)); },
        ["address"] = text => { _ = PostalAddress.TryParse(text, out _, out _); Expect(() => PostalAddress.Parse(text)); },
        ["person"] = text => { _ = PersonName.TryParse(text, out _, out _); Expect(() => PersonName.Parse(text)); },
        ["company"] = text => { _ = CompanyName.TryParse(text, out _, out _); Expect(() => CompanyName.Parse(text)); },
        ["normalize"] = text => { _ = Contacts.TryNormalize(text, Region.US, out _); Expect(() => Contacts.Normalize(text, Region.US)); },
    };

    private static int Main(string[] args)
    {
        if (args.Length == 4 && args[2] == "--seconds" && Targets.TryGetValue(args[0], out Action<string>? timed) && int.TryParse(args[3], out int seconds))
        {
            return Mutate(args[0], timed, args[1], seconds);
        }

        if (args.Length == 3 && args[0] == "names-smoke")
        {
            return NameSmoke.Run(args[1], int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture));
        }

        if (args.Length != 2 || !Targets.TryGetValue(args[0], out Action<string>? target))
        {
            Console.Error.WriteLine("usage: fuzz <" + string.Join('|', Targets.Keys) + "> <file|directory|->");
            return 64;
        }

        if (args[1] == "-")
        {
            using Stream stdin = Console.OpenStandardInput();
            using MemoryStream copy = new();
            stdin.CopyTo(copy);
            target(Encoding.UTF8.GetString(copy.ToArray()));
            return 0;
        }

        IEnumerable<string> files = Directory.Exists(args[1]) ? Directory.EnumerateFiles(args[1]) : [args[1]];
        int count = 0;
        foreach (string file in files)
        {
            target(Encoding.UTF8.GetString(File.ReadAllBytes(file)));
            count++;
        }

        Console.WriteLine($"{args[0]}: {count} input(s), no findings");
        return 0;
    }

    // The matcher must not throw, and every match must be the text it says it is, inside the input.
    private static void MatchAll(string text)
    {
        foreach (Region region in new[] { Region.US, Region.Parse("GB"), Region.Parse("DE"), Region.Unknown })
        {
            foreach (PhoneMatchLeniency leniency in Enum.GetValues<PhoneMatchLeniency>())
            {
                int previousEnd = 0;
                foreach (PhoneNumberMatch m in PhoneNumberMatcher.Default.FindAll(text, region, leniency))
                {
                    if (m.Start < previousEnd || m.End > text.Length || !string.Equals(text.Substring(m.Start, m.RawString.Length), m.RawString, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("a match does not lie in the text");
                    }

                    previousEnd = m.End;
                }
            }
        }
    }

    // The formatter takes any key presses; the remembered position stays inside the last output.
    private static void TypeAll(string text)
    {
        foreach (Region region in new[] { Region.US, Region.Parse("GB"), Region.Parse("BR"), Region.Unknown })
        {
            AsYouTypeFormatter formatter = new(region);
            int i = 0;
            foreach (char c in text)
            {
                string output = i++ % 7 == 3 ? formatter.InputDigitAndRememberPosition(c) : formatter.InputDigit(c);
                if (formatter.RememberedPosition < 0 || formatter.RememberedPosition > output.Length)
                {
                    throw new InvalidOperationException("the remembered position is outside the output");
                }
            }
        }
    }

    // The metadata loader may refuse with InvalidDataException and nothing else.
    private static void LoadMetadata(string text)
    {
        try
        {
            _ = PhoneMetadata.LoadXml(new MemoryStream(Encoding.UTF8.GetBytes(text)));
        }
        catch (InvalidDataException)
        {
        }
    }

    // A small in-process mutation loop: no coverage feedback, a fixed seed, byte flips, splices and truncations of the seed files.
    private static int Mutate(string name, Action<string> target, string seedDirectory, int seconds)
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

            string text = Encoding.UTF8.GetString(input);
            try
            {
                target(text);
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

    // Parse is allowed to refuse with ContactParseException and nothing else.
    private static void Expect(Action parse)
    {
        try
        {
            parse();
        }
        catch (ContactParseException)
        {
        }
    }
}
