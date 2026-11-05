using System.Globalization;
using RecursiveAI.OneContact.Oracles.Phones;

if (args.Length >= 2 && args[0] == "generate")
{
    int seed = args.Length >= 4 && args[2] == "--seed" ? int.Parse(args[3], CultureInfo.InvariantCulture) : 1;
    string metadata = args.Length >= 6 && args[4] == "--metadata" ? args[5] : Path.Combine(Environment.GetEnvironmentVariable("ONECONTACT_UPSTREAM") ?? ".upstream", "libphonenumber", "PhoneNumberMetadata.xml");
    int total = Generator.Run(args[1], seed, metadata);
    Console.WriteLine($"wrote {total} cases");
    return 0;
}

if (args.Length >= 3 && args[0] == "ours")
{
    Console.WriteLine($"answered {Ours.Run(args[1], args[2])} cases");
    return 0;
}

if (args.Length >= 4 && args[0] == "diff")
{
    int show = args.Length >= 6 && args[4] == "--show" ? int.Parse(args[5], CultureInfo.InvariantCulture) : 40;
    string? report = args.Length >= 8 && args[6] == "--report" ? args[7] : null;
    return Diff.Run(args[1], args[2], args[3], show, report);
}

Console.Error.WriteLine("usage: generate <cases.tsv> [--seed N] | ours <cases.tsv> <ours.tsv> | diff <ours.tsv> <jar.tsv> <cases.tsv> [--show N] [--report file]");
return 64;
