using System.Diagnostics;
using System.Globalization;
using RecursiveAI.OneContact.Phones;
using RecursiveAI.OneContact.Phones.Metadata;

namespace RecursiveAI.OneContact.Bench;

/// <summary>Times the pieces of a phone parse one by one, to see where the time goes. Run as <c>profile-phone</c>; the figures are nanoseconds per call.</summary>
internal static class PhoneProfile
{
    public static int Run()
    {
        const string input = "(202) 456-1111";
        const string nsn = "2024561111";
        PhoneMetadata md = PhoneMetadata.Default;
        RegionMetadata us = md.GetRegionMetadata(Region.US)!;
        Time("whole parse (US)", () => PhoneNumber.TryParse(input, Region.US, out _));
        Time("ExtractPossibleNumber", () => PhoneText.ExtractPossibleNumber(input, out _).Length);
        Time("IsViable", () => PhoneText.IsViable(input));
        Time("ValidAlpha.IsMatch", () => PhoneText.ValidAlpha.IsMatch(input));
        Time("Normalize", () => PhoneText.Normalize(input).Length);
        Time("StripExtension", () => PhoneText.StripExtension(input, out _).Length);
        Time("ExtractPhoneContext-free: IDD LookingAt", () => us.InternationalPrefixPattern!.Get().LookingAt(nsn, out _));
        Time("general.IsMatch", () => PhoneNumberParser.MatchesEntirely(us.GetDesc(DescKind.General), nsn));
        Time("TypeOf", () => PhoneNumberParser.TypeOf(nsn, us));
        Time("TestNumberLength", () => PhoneNumberParser.TestNumberLength(nsn, us, PhoneNumberType.Unknown));
        string p = nsn;
        Time("MaybeStripNationalPrefix", () => PhoneNumberParser.Default.MaybeStripNationalPrefixAndCarrierCode(ref p, us, out _));
        Time("Normalize(2024561111)", () => PhoneText.Normalize(nsn).Length);
        Time("GetRegionMetadata", () => md.GetRegionMetadata(Region.US));
        Time("RegionMetadataFor(1,nsn)", () => PhoneNumberParser.Default.RegionMetadataFor(1, nsn, out _));
        Time("PhoneFormatter.Canonical", () => PhoneFormatter.Canonical(1, 2024561111UL, 0, null));
        Time("ulong.Parse", () => ulong.Parse(nsn.AsSpan(0), NumberStyles.None, CultureInfo.InvariantCulture));
        PhoneNumber n = PhoneNumber.Parse(input, Region.US);
        Time("Format intl", () => n.Format(PhoneFormat.International).Length);
        Time("Format national", () => n.Format(PhoneFormat.National).Length);
        Time("IsValid", () => n.IsValid);
        return 0;
    }

    // Best of several batches: the machine is shared, and the minimum is the least disturbed figure.
    private static void Time<T>(string label, Func<T> work)
    {
        for (int i = 0; i < 20000; i++)
        {
            work();
        }

        const int reps = 30000;
        double best = double.MaxValue;
        long bytes = 0;
        for (int round = 0; round < 9; round++)
        {
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < reps; i++)
            {
                work();
            }

            sw.Stop();
            bytes = GC.GetAllocatedBytesForCurrentThread() - a0;
            best = Math.Min(best, sw.Elapsed.TotalMilliseconds * 1e6 / reps);
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{label,-40} {best,9:F1} ns  {bytes / reps,6} B"));
    }
}
