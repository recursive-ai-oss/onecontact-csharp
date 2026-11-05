using System.Text;

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
        ["phone"] = text => { _ = PhoneNumber.TryParse(text, Region.US, out _, out _); Expect(() => PhoneNumber.Parse(text, Region.US)); },
        ["email"] = text => { _ = EmailAddress.TryParse(text, out _, out _); Expect(() => EmailAddress.Parse(text)); },
        ["web"] = text => { _ = WebAddress.TryParse(text, out _, out _); Expect(() => WebAddress.Parse(text)); },
        ["address"] = text => { _ = PostalAddress.TryParse(text, out _, out _); Expect(() => PostalAddress.Parse(text)); },
        ["person"] = text => { _ = PersonName.TryParse(text, out _, out _); Expect(() => PersonName.Parse(text)); },
        ["company"] = text => { _ = CompanyName.TryParse(text, out _, out _); Expect(() => CompanyName.Parse(text)); },
        ["normalize"] = text => { _ = Contacts.TryNormalize(text, Region.US, out _); Expect(() => Contacts.Normalize(text, Region.US)); },
    };

    private static int Main(string[] args)
    {
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
