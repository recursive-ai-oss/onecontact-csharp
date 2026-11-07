using System.Buffers;
using BenchmarkDotNet.Attributes;
using RecursiveAI.OneContact.Addresses.Parsing;

namespace RecursiveAI.OneContact.Bench;

/// <summary>
/// United States free-text addresses (the US rows of the synthetic gold set, set ONECONTACT_ADDRESS_GOLD to its path), rules only without a model
/// package, with the country given as the United States (the comparator reads American addresses only) and, as a second row, without it, against the AddressParser regex parser. The comparator gets the lines joined with commas. The accuracy
/// the clause needs comes from the address gold tool on the same file.
/// </summary>
[MemoryDiagnoser]
public class AddressCompareRow
{
    private string[] _texts = [];
    private USAddress.AddressParser _theirs = new();

    /// <summary>Reads the US rows of the gold set.</summary>
    [GlobalSetup]
    public void Setup()
    {
        string path = Environment.GetEnvironmentVariable("ONECONTACT_ADDRESS_GOLD") ?? "testdata/addresses/parse-gold.tsv";
        _texts = File.ReadLines(path).Where(l => l.Length > 0 && l[0] != '#').Select(l => l.Split('\t')).Where(c => c[1] == "US").Select(c => c[2].Replace("\\n", "\n", StringComparison.Ordinal)).ToArray();
        _theirs = new USAddress.AddressParser();
    }

    /// <summary>This library told the addresses are American, which is what the comparator assumes.</summary>
    /// <returns>The number of addresses read.</returns>
    [Benchmark(OperationsPerInvoke = 100)]
    public int Ours()
    {
        int n = 0;
        for (int i = 0; i < 100; i++)
        {
            n += RuleAddressParser.TryParse(_texts[i % _texts.Length], Region.US, out _, out _) == OperationStatus.Done ? 1 : 0;
        }

        return n;
    }

    /// <summary>This library with no country given, which also works out the country of each address.</summary>
    /// <returns>The number of addresses read.</returns>
    [Benchmark(OperationsPerInvoke = 100)]
    public int OursNoHint()
    {
        int n = 0;
        for (int i = 0; i < 100; i++)
        {
            n += RuleAddressParser.TryParse(_texts[i % _texts.Length], Region.Unknown, out _, out _) == OperationStatus.Done ? 1 : 0;
        }

        return n;
    }

    /// <summary>AddressParser 1.0.3.</summary>
    /// <returns>The number of addresses read.</returns>
    [Benchmark(OperationsPerInvoke = 100)]
    public int AddressParserParse()
    {
        int n = 0;
        for (int i = 0; i < 100; i++)
        {
            n += _theirs.ParseAddress(_texts[i % _texts.Length].Replace("\n", ", ", StringComparison.Ordinal)) is not null ? 1 : 0;
        }

        return n;
    }
}
