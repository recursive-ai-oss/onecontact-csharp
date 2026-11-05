using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using RecursiveAI.OneContact;

namespace RecursiveAI.OneContact.Bench;

/// <summary>One trivial row, to prove the harness runs. Real rows are added per part, with matched comparators.</summary>
[MemoryDiagnoser]
public class RegionRow
{
    private readonly string _code = "de";

    /// <summary>Parses a region code.</summary>
    /// <returns>The region.</returns>
    [Benchmark]
    public Region ParseRegion() => Region.Parse(_code);
}

internal static class Program
{
    private static void Main(string[] args) => BenchmarkSwitcher.FromAssembly(typeof(RegionRow).Assembly).Run(args);
}
