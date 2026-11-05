using System.Diagnostics;
using RecursiveAI.OneContact.Names;

namespace RecursiveAI.OneContact.Fuzz;

/// <summary>
/// A bounded smoke run for the name and company parsers: mutates the committed rule cases at random (seeded), feeds every mutant to
/// the parsers and checks that nothing throws but <see cref="ContactParseException"/>, that every offset lies inside the input, and
/// that the canonical form of a parsed value parses again. Usage: <c>fuzz names-smoke &lt;testdata/names directory&gt; &lt;seconds&gt;</c>.
/// </summary>
internal static class NameSmoke
{
    private static readonly string[] Specials =
    [
        "\"", "'", "(", ")", ",", ";", "&", " and ", " und ", " y ", " c/o ", " d/b/a ", " t/a ", ".", "-", "\u2019", "\u201C", "\u201D", "0", "7", "@", "/",
        "\u0301", "\u0000", "\ud83d\ude00", "\u4e2d", "\u0130", "\u00DF", " ", "  ", "\t", "\n", " Jr.", ", PhD", "The ", "Dr. ", "van der ", "de la ",
    ];

    internal static int Run(string directory, int seconds)
    {
        List<string> seeds = [];
        foreach (string file in Directory.EnumerateFiles(directory, "*.psv"))
        {
            foreach (string line in File.ReadLines(file))
            {
                string[] c = line.Split('|');
                if (!line.StartsWith('#') && c.Length > 2)
                {
                    seeds.Add(c[2]);
                }
            }
        }

        foreach (string line in File.ReadLines(Path.Combine(directory, "gold.tsv")).Skip(1))
        {
            seeds.Add(line.Split('\t')[4]);
        }

        Random random = new(20261105);
        Stopwatch clock = Stopwatch.StartNew();
        long executions = 0;
        long parsedPersons = 0;
        long parsedCompanies = 0;
        NameHints[] hints = [default, new(Region.US), new(Region.Parse("ES"), "ca"), new(Region.Parse("HU")), new(Region.Parse("IE"), "ga"), new(Region.Parse("NL")), new(Region.Parse("DE"))];
        while (clock.Elapsed.TotalSeconds < seconds)
        {
            string input = Mutate(seeds, random);
            NameHints h = hints[random.Next(hints.Length)];
            try
            {
                NameParseResult r = NameParsing.Split(input, h);
                foreach (PersonName p in r.Persons)
                {
                    parsedPersons++;
                    CheckSpans(input, p);
                    _ = p.Key;
                    // Without hints the canonical form must read as a person again (the JSON converters read it that way). With hints it
                    // need not: a family-first language reads the given-first canonical form the other way round.
                    if (h == default && !PersonName.TryParse(p.Canonical, out _))
                    {
                        throw new InvalidOperationException("canonical form does not parse: " + p.Canonical);
                    }
                }

                if (r.Class == NameClass.Company)
                {
                    parsedCompanies++;
                    _ = r.Company.Key;
                    if (r.Company.Base.Span.End > input.Length)
                    {
                        throw new InvalidOperationException("company span outside the input");
                    }
                }

                _ = PersonName.TryParse(input, h, out _, out _);
                _ = CompanyName.TryParse(input, h, out CompanyName cn, out _);
                if (!cn.Canonical.AsSpan().IsEmpty && CompanyName.TryParse(cn.Canonical, h, out _, out _) != System.Buffers.OperationStatus.Done)
                {
                    throw new InvalidOperationException("company canonical form does not parse: " + cn.Canonical);
                }

                try
                {
                    _ = PersonName.Parse(input, h);
                }
                catch (ContactParseException)
                {
                }
            }
            catch (Exception e) when (e is not ContactParseException)
            {
                Console.Error.WriteLine($"FINDING after {executions} executions on input: {System.Text.Json.JsonSerializer.Serialize(input)}\n{e}");
                return 1;
            }

            executions++;
        }

        Console.WriteLine($"names-smoke: {executions} executions in {seconds} s, {seeds.Count} seeds, {parsedPersons} persons and {parsedCompanies} companies parsed, no findings");
        return 0;
    }

    private static void CheckSpans(string input, PersonName p)
    {
        foreach (NamePart part in new[] { p.Prefix, p.Given, p.Middle, p.Nickname, p.Particle, p.Family, p.SecondFamily, p.Suffix, p.ProfessionalSuffix })
        {
            if (part.IsEmpty)
            {
                continue;
            }

            if (part.Span.Start < 0 || part.Span.Length <= 0 || part.Span.End > input.Length)
            {
                throw new InvalidOperationException($"span {part.Span} outside the input of {input.Length}");
            }
        }
    }

    private static string Mutate(List<string> seeds, Random random)
    {
        string s = seeds[random.Next(seeds.Count)];
        int steps = random.Next(0, 5);
        for (int i = 0; i < steps; i++)
        {
            switch (random.Next(7))
            {
                case 0:
                    s = s.Insert(random.Next(s.Length + 1), Specials[random.Next(Specials.Length)]);
                    break;
                case 1 when s.Length > 0:
                    s = s.Remove(random.Next(s.Length), 1);
                    break;
                case 2:
                    s += " " + seeds[random.Next(seeds.Count)];
                    break;
                case 3 when s.Length > 0:
                    s = s[..random.Next(s.Length)];
                    break;
                case 4:
                    s = s.ToUpperInvariant();
                    break;
                case 5:
                    s = s.ToLowerInvariant();
                    break;
                default:
                    s = seeds[random.Next(seeds.Count)] + (random.Next(2) == 0 ? ", " : " & ") + s;
                    break;
            }
        }

        return s;
    }
}
