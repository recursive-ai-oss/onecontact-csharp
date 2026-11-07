using System.Diagnostics;
using System.Globalization;
using RecursiveAI.OneContact.Text.Idna;

namespace RecursiveAI.OneContact.Bench;

/// <summary>
/// Times the internationalized email addresses of the comparison row and the IDNA conversion under them, with allocations per call.
/// Run as <c>profile-idn</c>; figures are nanoseconds per call, best of several batches.
/// </summary>
internal static class IdnProfile
{
    private const Uts46Options EmailOptions = Uts46Options.UseStd3AsciiRules | Uts46Options.CheckHyphens | Uts46Options.CheckBidi | Uts46Options.CheckJoiners;

    public static int Run()
    {
        Console.WriteLine("-- ns per call | bytes per call");
        foreach ((string text, _) in Inputs.EmailsIdn)
        {
            string domain = text[(text.IndexOf('@') + 1)..];
            Report("email   " + text, () => EmailAddress.TryParse(text, out _));
            Report("toascii " + domain, () => Uts46.TryToAscii(domain, EmailOptions, out _));
        }

        return 0;
    }

    private static void Report(string label, Func<bool> work)
    {
        // The JIT promotes hot code on a background timer, so warm up for a fixed time rather than a fixed count.
        var warm = Stopwatch.StartNew();
        while (warm.ElapsedMilliseconds < 600)
        {
            for (int i = 0; i < 1000; i++)
            {
                work();
            }
        }

        const int reps = 20000;
        double best = double.MaxValue;
        for (int round = 0; round < 7; round++)
        {
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < reps; i++)
            {
                work();
            }

            sw.Stop();
            best = Math.Min(best, sw.Elapsed.TotalMilliseconds * 1e6 / reps);
        }

        long a0 = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < reps; i++)
        {
            work();
        }

        double bytes = (GC.GetAllocatedBytesForCurrentThread() - a0) / (double)reps;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{best,7:F0} {bytes,7:F0}  {label}"));
    }
}
