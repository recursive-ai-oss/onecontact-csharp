using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;
using System.Text.RegularExpressions;
using RecursiveAI.OneContact;
using RecursiveAI.OneContact.Phones;
using RecursiveAI.OneContact.Phones.Metadata;

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

/// <summary>
/// Metadata pattern matching: the matcher the phone core uses against a source-generated Regex, on general-description patterns of
/// three regions (short, medium, long alternation). Whole-input matches of valid and invalid national numbers.
/// </summary>
[MemoryDiagnoser]
public partial class PatternRow
{
    private const string Us = @"[2-9]\d{9}|3\d{6}";
    private const string Gb = @"[1-357-9]\d{9}|[18]\d{8}|8\d{6}";
    private const string De = @"[2579]\d{5,14}|49(?:[34]0|69|8\d)\d\d?|49(?:37|49|60|7[089]|9\d)\d{1,3}|49(?:2[024-9]|3[2-689]|7[1-7])\d{1,8}|(?:1|[368]\d|4[0-8])\d{3,13}|49(?:[015]\d|2[13]|31|[46][1-8])\d{1,9}";

    private readonly string[] _inputs = ["2024561111", "442079460958", "4930123456", "12345", "9999999999999999"];
    private readonly PhoneRegex _us = PhoneRegex.Compile(Us);
    private readonly PhoneRegex _gb = PhoneRegex.Compile(Gb);
    private readonly PhoneRegex _de = PhoneRegex.Compile(De);

    /// <summary>Own matcher, three patterns over five inputs.</summary>
    /// <returns>The number of matches.</returns>
    [Benchmark(Baseline = true)]
    public int OwnMatcher()
    {
        int n = 0;
        foreach (string input in _inputs)
        {
            n += (_us.IsMatch(input) ? 1 : 0) + (_gb.IsMatch(input) ? 1 : 0) + (_de.IsMatch(input) ? 1 : 0);
        }

        return n;
    }

    /// <summary>Source-generated Regex, same patterns and inputs, anchored.</summary>
    /// <returns>The number of matches.</returns>
    [Benchmark]
    public int GeneratedRegexMatcher()
    {
        int n = 0;
        foreach (string input in _inputs)
        {
            n += (UsRegex().IsMatch(input) ? 1 : 0) + (GbRegex().IsMatch(input) ? 1 : 0) + (DeRegex().IsMatch(input) ? 1 : 0);
        }

        return n;
    }

    [GeneratedRegex(@"^(?:[2-9]\d{9}|3\d{6})$", RegexOptions.CultureInvariant)]
    private static partial Regex UsRegex();

    [GeneratedRegex(@"^(?:[1-357-9]\d{9}|[18]\d{8}|8\d{6})$", RegexOptions.CultureInvariant)]
    private static partial Regex GbRegex();

    [GeneratedRegex(@"^(?:[2579]\d{5,14}|49(?:[34]0|69|8\d)\d\d?|49(?:37|49|60|7[089]|9\d)\d{1,3}|49(?:2[024-9]|3[2-689]|7[1-7])\d{1,8}|(?:1|[368]\d|4[0-8])\d{3,13}|49(?:[015]\d|2[13]|31|[46][1-8])\d{1,9})$", RegexOptions.CultureInvariant)]
    private static partial Regex DeRegex();
}

/// <summary>End-to-end phone operations on the default metadata.</summary>
[MemoryDiagnoser]
public class PhoneRow
{
    private readonly string _text = "(202) 456-1111";
    private readonly string _gb = "020 7946 0958";

    /// <summary>Parses a US national number.</summary>
    /// <returns>The number.</returns>
    [Benchmark]
    public PhoneNumber Parse() => PhoneNumber.Parse(_text, Region.US);

    /// <summary>Parses and formats internationally.</summary>
    /// <returns>The text.</returns>
    [Benchmark]
    public string ParseAndFormat() => PhoneNumber.Parse(_text, Region.US).Format(PhoneFormat.International);

    /// <summary>Parses and validates a UK number.</summary>
    /// <returns>Whether it is valid.</returns>
    [Benchmark]
    public bool ParseAndValidate() => PhoneNumber.Parse(_gb, Region.Parse("GB")).IsValid;
}

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "profile-extract")
        {
            return Profiles.Extract();
        }

        if (args.Length > 0 && args[0] == "profile-names")
        {
            return Profiles.Names();
        }

        if (args.Length > 0 && args[0] == "profile-phone")
        {
            return PhoneProfile.Run();
        }

        if (args.Length == 3 && args[0] == "dump-matcher")
        {
            // dump-matcher <text file> <matches file>: the matcher row's text and where our matcher finds numbers in it (start, length).
            string text = PhoneMatcherRow.MakeText();
            File.WriteAllText(args[1], text, new System.Text.UTF8Encoding(false));
            File.WriteAllLines(args[2], PhoneNumber.FindAll(text, Region.US, PhoneMatchLeniency.Valid).Select(m => $"{m.Start}\t{m.RawString.Length}"));
            return 0;
        }

        if (args.Length > 0 && args[0] == "profile-compare")
        {
            return CompareProfile.Run();
        }

        if (args.Length > 0 && args[0] == "loop-extract")
        {
            // loop-extract <text|html|phones> <seconds>: runs one extraction workload for a fixed time, for an external sampling profiler.
            return Profiles.Loop(args[1], int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture));
        }

        if (args.Length == 3 && args[0] == "dump-diff")
        {
            return DiffDump.Run(args[1], args[2]);
        }

        if (args.Length > 0 && args[0] == "profile-idn")
        {
            return IdnProfile.Run();
        }

        if (args.Length > 0 && args[0] == "profile-find")
        {
            return PhoneFindProfile.Run();
        }

        if (args.Length > 0 && args[0] == "accuracy")
        {
            return Accuracy.Run();
        }

        // The in-process toolchain: the assembly name differs from the project name, so a generated child project cannot find it.
        ManualConfig config = DefaultConfig.Instance.AddJob((Environment.GetEnvironmentVariable("ONECONTACT_BENCH_JOB") == "default" ? Job.Default : Job.ShortRun).WithToolchain(InProcessNoEmitToolchain.Instance));
        BenchmarkSwitcher.FromAssembly(typeof(RegionRow).Assembly).Run(args, config);
        return 0;
    }
}
