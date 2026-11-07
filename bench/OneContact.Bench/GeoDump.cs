using System.Globalization;
using System.Text;
using RecursiveAI.OneContact.Phones;
using RecursiveAI.OneContact.Phones.Geo;

namespace RecursiveAI.OneContact.Bench;

/// <summary>
/// Writes what the geocoder, carrier and time-zone mappers answer for seeded numbers of every region, so two builds can be compared byte for
/// byte. Run as <c>dump-geo &lt;file&gt;</c>.
/// </summary>
internal static class GeoDump
{
    /// <summary>Writes the answers.</summary>
    /// <param name="path">The output file.</param>
    /// <returns>Zero.</returns>
    public static int Run(string path)
    {
        var rng = new Random(64);
        using var w = new StreamWriter(path, false, new UTF8Encoding(false));
        string[] languages = ["en", "de", "fr", "es", "zh-Hant", "ja", "ru"];
        foreach (Region region in PhoneNumberParser.Default.Metadata.Regions.OrderBy(r => r.Code, StringComparer.Ordinal))
        {
            string cc = region.Code;
            for (int i = 0; i < 150; i++)
            {
                int len = 5 + rng.Next(8);
                var digits = new StringBuilder();
                for (int k = 0; k < len; k++)
                {
                    digits.Append((char)('0' + rng.Next(10)));
                }

                if (!PhoneNumber.TryParse(digits.ToString(), region, out PhoneNumber p))
                {
                    continue;
                }

                var line = new StringBuilder();
                line.Append(cc).Append('|').Append(digits).Append('|');
                foreach (string language in languages)
                {
                    line.Append(PhoneGeocoder.Default.GetDescription(p, language)).Append(';')
                        .Append(PhoneGeocoder.Default.GetDescription(p, language, Region.US)).Append(';')
                        .Append(PhoneCarrierMapper.Default.GetName(p, language)).Append(';');
                }

                line.Append(string.Join(',', PhoneTimeZoneMapper.Default.GetTimeZones(p)));
                w.WriteLine(line.ToString());
            }
        }

        w.Flush();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"wrote {path}"));
        return 0;
    }
}
