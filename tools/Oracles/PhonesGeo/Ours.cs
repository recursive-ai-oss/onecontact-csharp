using System.Text;
using RecursiveAI.OneContact.Phones.Geo;

namespace RecursiveAI.OneContact.Oracles.PhonesGeo;

/// <summary>Answers the cases with OneContact. A case is <c>id, calling code, national number, language tag, user region</c> (<c>-</c> for none); an answer is the id and nine fields.</summary>
internal static class Ours
{
    internal static int Run(string casesPath, string outPath)
    {
        PhoneGeocoder geo = PhoneGeocoder.Default;
        PhoneCarrierMapper carrier = PhoneCarrierMapper.Default;
        PhoneTimeZoneMapper zones = PhoneTimeZoneMapper.Default;
        using StreamWriter w = new(outPath, false, new UTF8Encoding(false));
        w.NewLine = "\n";
        int count = 0;
        foreach (string line in File.ReadLines(casesPath))
        {
            string[] f = line.Split('\t');
            PhoneNumber n = PhoneNumber.Create(int.Parse(f[1], System.Globalization.CultureInfo.InvariantCulture), f[2]);
            string tag = Unescape(f[3]);
            bool hasRegion = f[4] != "-";
            Region user = hasRegion ? Region.Parse(f[4]) : Region.Unknown;
            string[] fields =
            [
                geo.GetDescription(n, tag),
                hasRegion ? geo.GetDescription(n, tag, user) : "-",
                geo.GetDescriptionForValidNumber(n, tag),
                hasRegion ? geo.GetDescriptionForValidNumber(n, tag, user) : "-",
                carrier.GetName(n, tag),
                PhoneCarrierMapper.GetNameForValidNumber(n, tag),
                carrier.GetSafeDisplayName(n, tag),
                string.Join('&', zones.GetTimeZones(n)),
                string.Join('&', zones.GetTimeZonesForGeographicalNumber(n)),
            ];
            w.Write(f[0]);
            foreach (string v in fields)
            {
                w.Write('\t');
                w.Write(Escape.Write(v));
            }

            w.WriteLine();
            count++;
        }

        return count;
    }

    private static string Unescape(string s)
    {
        StringBuilder sb = new();
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\')
            {
                if (s[i + 1] == '\\')
                {
                    sb.Append('\\');
                    i++;
                }
                else
                {
                    sb.Append((char)int.Parse(s.AsSpan(i + 2, 4), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture));
                    i += 5;
                }
            }
            else
            {
                sb.Append(s[i]);
            }
        }

        return sb.ToString();
    }
}
