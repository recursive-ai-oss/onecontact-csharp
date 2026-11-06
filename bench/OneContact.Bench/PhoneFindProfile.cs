using System.Diagnostics;
using System.Globalization;
using RecursiveAI.OneContact.Phones;
using RecursiveAI.OneContact.Phones.Metadata;

namespace RecursiveAI.OneContact.Bench;

/// <summary>Times the stages of the number finder over the matcher row's text, to see where the time goes. Run as <c>profile-find</c>.</summary>
internal static class PhoneFindProfile
{
    public static int Run()
    {
        string text = PhoneMatcherRow.MakeText();
        var spans = new List<(int Start, int End)>();
        int at = 0;
        while (PhoneNumberMatcher.Candidate.Find(text, at, default, out int s, out int e))
        {
            spans.Add((s, e));
            at = Math.Max(e, s + 1);
        }

        Console.WriteLine($"text {text.Length} chars, {spans.Count} candidates");
        string[] candidates = [.. spans.Select(x => text[x.Start..x.End])];
        Time("Candidate.Find over the text", () =>
        {
            int n = 0;
            int p = 0;
            while (PhoneNumberMatcher.Candidate.Find(text, p, default, out int s, out int e))
            {
                n++;
                p = Math.Max(e, s + 1);
            }

            return n;
        });
        Time("SecondNumberStart.Find", () => Each(candidates, c => PhoneText.SecondNumberStart.Find(c, 0, default, out _, out _)));
        Time("SlashSeparatedDates.Find", () => Each(candidates, c => PhoneNumberMatcher.SlashSeparatedDates.Find(c, 0, default, out _, out _)));
        Time("TimeStamps.Find", () => Each(candidates, c => PhoneNumberMatcher.TimeStamps.Find(c, 0, default, out _, out _)));
        Time("MatchingBrackets.IsMatch", () => Each(candidates, c => PhoneNumberMatcher.MatchingBrackets.IsMatch(c)));
        Time("PubPages.Find", () => Each(candidates, c => PhoneNumberMatcher.PubPages.Find(c, 0, default, out _, out _)));
        PhoneNumberParser parser = PhoneNumberParser.Default;
        Time("parser.TryParse(keepRaw, detail)", () => Each(candidates, c => parser.TryParse(c, Region.US, true, out _, out _) == System.Buffers.OperationStatus.Done));
        Time("PhoneNumber.TryParse (quick)", () => Each(candidates, c => PhoneNumber.TryParse(c, Region.US, out _)));
        Time("FindAll Possible (whole text)", () => PhoneNumber.FindAll(text, Region.US, PhoneMatchLeniency.Possible).Count());
        Time("FindAll Valid (whole text)", () => PhoneNumber.FindAll(text, Region.US, PhoneMatchLeniency.Valid).Count());
        Time("FindAll ExactGrouping (whole text)", () => PhoneNumber.FindAll(text, Region.US, PhoneMatchLeniency.ExactGrouping).Count());
        return 0;
    }

    private static int Each(string[] items, Func<string, bool> f)
    {
        int n = 0;
        foreach (string s in items)
        {
            if (f(s))
            {
                n++;
            }
        }

        return n;
    }

    private static void Time<T>(string label, Func<T> work)
    {
        work();
        work();
        double best = double.MaxValue;
        long bytes = 0;
        for (int round = 0; round < 7; round++)
        {
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            T r = work();
            sw.Stop();
            bytes = GC.GetAllocatedBytesForCurrentThread() - a0;
            best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
            if (round == 0)
            {
                Console.Write(string.Create(CultureInfo.InvariantCulture, $"[{r}] "));
            }
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{label,-40} {best,9:F2} ms  {bytes / 1024.0,9:F0} KB"));
    }
}
