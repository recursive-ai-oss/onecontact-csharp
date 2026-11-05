using System.Diagnostics;
using System.Buffers;
using RecursiveAI.OneContact.Addresses;
using RecursiveAI.OneContact.Addresses.Parsing;
using RecursiveAI.OneContact.Addresses.Standardization;
using RecursiveAI.OneContact.Keys;

namespace RecursiveAI.OneContact.Fuzz;

/// <summary>
/// A bounded smoke run for the address rule parser over text in the style of France, Monaco, Belgium, the Netherlands, Luxembourg, Germany, Austria, Switzerland,
/// Liechtenstein, the United Kingdom with Jersey, Guernsey and the Isle of Man, Ireland, Spain, Portugal, Italy, San Marino, the Vatican, Malta and Andorra: mutates the committed guide examples at random (seeded), feeds every mutant to the parser with each country as the hint and without one, and checks that
/// nothing throws, that every field's offsets lie inside the input and slice to its text, and that both renderings and the key can be computed.
/// Usage: <c>fuzz address-smoke &lt;example files, separated by a semicolon&gt; &lt;seconds&gt;</c>; the Tier 2 example files are testdata/addresses/authority-examples-tier2a.tsv and -tier2b.tsv.
/// </summary>
internal static class AddressSmoke
{
    private static readonly string[] Specials =
    [
        "\n", ", ", " / ", " - ", "L-", "CH-", "FL-", "D-", "B-", "F-", "MC-", " CEDEX ", " BP ", "Postfach ", "Postbus ", " bus ", " bte ", " // ", "0", "9", "98000", "1234 AB", "\u0000",
        "\u0301", "\u00DF", "\u00E9", "\ud83d\ude00", "\u4e2d", "\t", "  ", "'", "\u2019", ".", "-", "\r\n", "FRANCE", "BELGIUM", "Switzerland", "SW1A 1AA", "T37 F8HK", "HMR 1428", "AD500", "2725–079", "00120", "47890", "3º, C", "CO. ", " RM", "(Madrid)", "Apartado ", "P.O. Box ", "UNITED KINGDOM", "CHANNEL ISLANDS", "15,500",
    ];

    private static readonly string[] Countries = ["FR", "MC", "GP", "BE", "NL", "LU", "DE", "AT", "CH", "LI", "GB", "JE", "GG", "IM", "IE", "ES", "PT", "IT", "SM", "VA", "MT", "AD"];

    internal static int Run(string examples, int seconds)
    {
        List<string> seeds = [];
        foreach (string line in examples.Split(';').SelectMany(File.ReadLines))
        {
            string[] c = line.Split('\t');
            if (line.Length > 0 && line[0] != '#' && c.Length > 4)
            {
                seeds.Add(c[4].Replace("\\n", "\n", StringComparison.Ordinal));
            }
        }

        Region[] hints = [default, .. Countries.Select(Region.Parse)];
        Random random = new(20261105);
        Stopwatch clock = Stopwatch.StartNew();
        long executions = 0;
        long parsed = 0;
        while (clock.Elapsed.TotalSeconds < seconds)
        {
            string input = Mutate(seeds, random);
            Region hint = hints[random.Next(hints.Length)];
            try
            {
                if (RuleAddressParser.TryParse(input, hint, out ParsedAddress? a, out _) == OperationStatus.Done && a is not null)
                {
                    parsed++;
                    foreach (ParsedAddressField f in a.Fields)
                    {
                        if (f.Span.Start < 0 || f.Span.End > input.Length || (f.Name != "country" && string.CompareOrdinal(input, f.Span.Start, f.Text, 0, f.Text.Length) != 0))
                        {
                            return Fail(input, hint, "offset outside the input or not the field text: " + f.Name);
                        }
                    }

                    _ = a.ToString(AddressStyle.Postal, AddressLayout.Label, includeCountry: true);
                    _ = a.ToString(AddressStyle.Display, AddressLayout.SingleLine, includeCountry: false);
                    _ = AddressKey.Compute(a, KeyVersion.V1);
                }

                _ = PostalAddress.TryParse(input, hint.IsUnknown ? Region.Parse("FR") : hint, out _, out _);
            }
            catch (Exception e)
            {
                return Fail(input, hint, e.GetType().Name + ": " + e.Message);
            }

            executions++;
        }

        Console.WriteLine($"address-smoke: {executions} executions, {parsed} parsed, no failures, {clock.Elapsed.TotalSeconds:F0} s");
        return 0;
    }

    private static int Fail(string input, Region hint, string why)
    {
        Console.Error.WriteLine($"FAIL {why}\nhint {hint}\ninput {System.Text.Json.JsonSerializer.Serialize(input)}");
        return 1;
    }

    private static string Mutate(List<string> seeds, Random random)
    {
        string s = seeds[random.Next(seeds.Count)];
        int rounds = 1 + random.Next(4);
        for (int i = 0; i < rounds; i++)
        {
            int at = s.Length == 0 ? 0 : random.Next(s.Length + 1);
            switch (random.Next(5))
            {
                case 0 when s.Length > 0:
                    s = s.Remove(random.Next(s.Length), 1);
                    break;
                case 1:
                    s = s.Insert(at, Specials[random.Next(Specials.Length)]);
                    break;
                case 2:
                    s = s[..at];
                    break;
                case 3:
                    s = s.Insert(at, seeds[random.Next(seeds.Count)]);
                    break;
                default:
                    if (s.Length > 1)
                    {
                        int a = random.Next(s.Length - 1);
                        s = s[..a] + s[(a + 1)..] + s[a];
                    }

                    break;
            }
        }

        return s;
    }
}
