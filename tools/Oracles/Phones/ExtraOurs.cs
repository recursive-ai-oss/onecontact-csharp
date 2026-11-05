using System.Text;
using RecursiveAI.OneContact.Phones;

namespace RecursiveAI.OneContact.Oracles.Phones;

/// <summary>Answers the cases ExtraGen wrote, in the format ExtraDriver.java writes.</summary>
internal static class ExtraOurs
{
    private static string Bool(bool b) => b ? "1" : "0";

    private static string CostName(ShortNumberCost c) => c switch
    {
        ShortNumberCost.TollFree => "TOLL_FREE",
        ShortNumberCost.StandardRate => "STANDARD_RATE",
        ShortNumberCost.PremiumRate => "PREMIUM_RATE",
        _ => "UNKNOWN_COST",
    };

    private static void Emit(TextWriter w, string id, string op, Func<string> body)
    {
        string value;
        try
        {
            value = body();
        }
#pragma warning disable CA1031 // The harness reports any failure of the code under test as a result instead of stopping.
        catch (Exception e)
#pragma warning restore CA1031
        {
            value = "EXC:" + e.GetType().Name;
        }

        w.Write(id + "\t" + op + "\t" + value + "\n");
    }

    internal static int Run(string casesPath, string outPath)
    {
        using StreamWriter w = new(outPath, false, new UTF8Encoding(false));
        int count = 0;
        foreach (string line in File.ReadLines(casesPath))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] f = line.Split('\t');
            string id = f[0];
            Region region = Region.TryParse(f[2], out Region r) ? r : Region.Unknown;
            switch (f[1])
            {
                case "S":
                    Short(w, id, region, Escape.Decode(f[3]));
                    break;
                case "E":
                    Emit(w, id, "emerg.connects", () => Bool(ShortNumberInfo.Default.ConnectsToEmergencyNumber(Escape.Decode(f[3]), region)));
                    Emit(w, id, "emerg.is", () => Bool(ShortNumberInfo.Default.IsEmergencyNumber(Escape.Decode(f[3]), region)));
                    break;
                default:
                    ExtraKinds(w, id, f[1], region, Escape.Decode(f[3]));
                    break;
            }

            count++;
        }

        return count;
    }

    private static void Short(TextWriter w, string id, Region region, string digits)
    {
        ShortNumberInfo s = ShortNumberInfo.Default;
        int cc = PhoneMetadata.Default.GetCallingCode(region);
        if (!PhoneNumber.TryParse("+" + cc + digits, Region.Unknown, out PhoneNumber n))
        {
            Emit(w, id, "short.parse", () => "ERR");
            return;
        }

        Emit(w, id, "short.parse", () => Ours.Parts(n, false));
        Emit(w, id, "short.possible", () => Bool(s.IsPossible(n)));
        Emit(w, id, "short.possible.region", () => Bool(s.IsPossibleForRegion(n, region)));
        Emit(w, id, "short.valid", () => Bool(s.IsValid(n)));
        Emit(w, id, "short.valid.region", () => Bool(s.IsValidForRegion(n, region)));
        Emit(w, id, "short.cost", () => CostName(s.GetExpectedCost(n)));
        Emit(w, id, "short.cost.region", () => CostName(s.GetExpectedCostForRegion(n, region)));
        Emit(w, id, "short.carrier", () => Bool(s.IsCarrierSpecific(n)));
        Emit(w, id, "short.carrier.region", () => Bool(s.IsCarrierSpecificForRegion(n, region)));
        Emit(w, id, "short.sms.region", () => Bool(s.IsSmsServiceForRegion(n, region)));
    }

    private static void ExtraKinds(TextWriter w, string id, string kind, Region region, string text)
    {
        switch (kind)
        {
            case "A":
                AsYouType(w, id, region, text);
                break;
            case "M":
                Matcher(w, id, region, text);
                break;
            default:
                throw new InvalidOperationException("unknown case kind " + kind);
        }
    }

    private static void AsYouType(TextWriter w, string id, Region region, string text)
    {
        Emit(w, id, "ayt", () =>
        {
            AsYouTypeFormatter f = new(region);
            StringBuilder sb = new();
            foreach (char c in text)
            {
                sb.Append(Escape.Encode(f.InputDigit(c))).Append('|');
            }

            return sb.ToString();
        });
        Emit(w, id, "ayt.rem", () =>
        {
            AsYouTypeFormatter f = new(region);
            int k = text.Length / 2;
            StringBuilder sb = new();
            StringBuilder pos = new();
            for (int i = 0; i < text.Length; i++)
            {
                sb.Append(Escape.Encode(i == k ? f.InputDigitAndRememberPosition(text[i]) : f.InputDigit(text[i]))).Append('|');
                pos.Append(f.RememberedPosition).Append(',');
            }

            return sb + "@" + pos;
        });
        Emit(w, id, "ayt.clear", () =>
        {
            AsYouTypeFormatter f = new(region);
            foreach (char c in text)
            {
                f.InputDigit(c);
            }

            f.Clear();
            StringBuilder sb = new();
            foreach (char c in text)
            {
                sb.Append(Escape.Encode(f.InputDigit(c))).Append('|');
            }

            return sb.ToString();
        });
    }

    private static string Matches(string text, Region region, PhoneMatchLeniency leniency, long tries)
    {
        StringBuilder sb = new();
        foreach (PhoneNumberMatch m in PhoneNumberMatcher.Default.FindAll(text, region, leniency, tries))
        {
            sb.Append(m.Start).Append('-').Append(m.End).Append(':').Append(Escape.Encode(m.RawString)).Append(':').Append(Ours.Parts(m.Number, false)).Append('|');
        }

        return sb.ToString();
    }

    private static void Matcher(TextWriter w, string id, Region region, string text)
    {
        (string Name, PhoneMatchLeniency Level)[] levels =
        [
            ("POSSIBLE", PhoneMatchLeniency.Possible), ("VALID", PhoneMatchLeniency.Valid),
            ("STRICT_GROUPING", PhoneMatchLeniency.StrictGrouping), ("EXACT_GROUPING", PhoneMatchLeniency.ExactGrouping),
        ];
        foreach ((string name, PhoneMatchLeniency level) in levels)
        {
            Emit(w, id, "m." + name, () => Matches(text, region, level, long.MaxValue));
        }

        Emit(w, id, "m.VALID.t1", () => Matches(text, region, PhoneMatchLeniency.Valid, 1));
        Emit(w, id, "m.POSSIBLE.t3", () => Matches(text, region, PhoneMatchLeniency.Possible, 3));
        Emit(w, id, "m.EXACT_GROUPING.t2", () => Matches(text, region, PhoneMatchLeniency.ExactGrouping, 2));
    }
}
