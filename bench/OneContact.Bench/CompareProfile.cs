using System.Diagnostics;
using System.Globalization;
using System.Net.Mail;
using AngleSharp.Dom;
using MimeKit;
using RecursiveAI.OneContact.Web;

namespace RecursiveAI.OneContact.Bench;

/// <summary>
/// Times each email address and each URL of the comparison rows on its own, ours beside the comparators, to show which inputs carry the
/// average. Run as <c>profile-compare</c>; figures are nanoseconds per call, best of several batches.
/// </summary>
internal static class CompareProfile
{
    public static int Run()
    {
        Console.WriteLine("-- email: ours | MailAddress | MimeKit (ns per parse)");
        foreach ((string text, _) in Inputs.Emails)
        {
            double ours = Best(() => EmailAddress.TryParse(text, out _));
            double mail = Best(() => MailAddress.TryCreate(text, out _));
            double mime = Best(() => MailboxAddress.TryParse(text, out _));
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{ours,7:F0} {mail,7:F0} {mime,7:F0}  {text}"));
        }

        Console.WriteLine("-- url: ours | AngleSharp (ns per parse)");
        foreach (string text in Inputs.Urls)
        {
            double ours = Best(() => WebAddress.TryParse(text, out _));
            double angle = Best(() => new Url(text).IsInvalid);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{ours,7:F0} {angle,7:F0}  {text}"));
        }

        return 0;
    }

    private static double Best(Func<bool> work)
    {
        for (int i = 0; i < 5000; i++)
        {
            work();
        }

        const int reps = 20000;
        double best = double.MaxValue;
        for (int round = 0; round < 5; round++)
        {
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < reps; i++)
            {
                work();
            }

            sw.Stop();
            best = Math.Min(best, sw.Elapsed.TotalMilliseconds * 1e6 / reps);
        }

        return best;
    }
}
