using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using OneContact.Data.AddressTier2A;
using OneContact.Data.AddressUsCa;

namespace OneContact.Data.AddressTier2C;

internal static partial class Registers
{
    /// <summary>Reads a plain table written by <c>convert</c> (see <see cref="Derivation"/>).</summary>
    private static IEnumerable<Addr> Derived(string country, string language, string path, Rejects rejects)
    {
        using FileStream fs = File.OpenRead(path);
        using GZipStream gz = new(fs, CompressionMode.Decompress);
        using StreamReader sr = new(gz, new UTF8Encoding(false), false, 1 << 20);
        string? header = sr.ReadLine();
        if (header != Derivation.Header)
        {
            throw new InvalidDataException(path + ": unexpected columns");
        }

        string? line;
        while ((line = sr.ReadLine()) is not null)
        {
            string[] c = line.Split('\t');
            if (c.Length != 12)
            {
                rejects.Add(country.ToLowerInvariant() + ": short row");
                continue;
            }

            yield return new Addr
            {
                Id = c[0],
                Country = country,
                Region = c[1],
                Language = language,
                Street = c[2],
                StreetType = c[3],
                HouseNumber = c[4],
                Suffix = c[5],
                Unit = c[6],
                AltNumber = c[7],
                Locality = c[8],
                Dependent = c[9],
                Admin = c[10],
                PostalCode = c[11],
            };
        }
    }

    [GeneratedRegex(@"^\d{1,4}[A-Za-z]?(?:-\d{1,4}[A-Za-z]?)?$", RegexOptions.CultureInvariant)]
    private static partial Regex LithuanianNumber();

    // Address register open data (Registrų centras, CC BY 4.0) as packaged by geoportal.lt, converted from its geodatabase by convert-lt-gdb.py.
    // adresas is the register's own address text: "Suvalkiečių g. 15, korpusas 7, LT-68227 Marijampolė"; in a village without streets the village
    // name stands where the street would be ("Misviečiai 4, LT-70375"). GYV_PAV is the settlement in the genitive with its kind ("Akmeniškių k."),
    // SAV_PAV the municipality. Flats come from the register's room table.
    private static IEnumerable<Addr> Lithuania(string directory, Rejects rejects)
    {
        string derived = Path.Combine(directory, "derived");
        Dictionary<string, List<string>> flats = new(StringComparer.Ordinal);
        using (StreamReader fr = new(new GZipStream(File.OpenRead(Path.Combine(derived, "lt-flats.tsv.gz")), CompressionMode.Decompress), new UTF8Encoding(false)))
        {
            fr.ReadLine();
            string? l;
            while ((l = fr.ReadLine()) is not null)
            {
                string[] f = l.Split('\t');
                if (f.Length == 2 && f[1].Length is > 0 and <= 5 && f[1].All(char.IsAsciiDigit))
                {
                    if (!flats.TryGetValue(f[0], out List<string>? list))
                    {
                        flats[f[0]] = list = [];
                    }

                    list.Add(f[1]);
                }
            }
        }

        using StreamReader sr = new(new GZipStream(File.OpenRead(Path.Combine(derived, "lt-addresses.tsv.gz")), CompressionMode.Decompress), new UTF8Encoding(false), false, 1 << 20);
        sr.ReadLine();
        string? line;
        while ((line = sr.ReadLine()) is not null)
        {
            string[] c = line.Split('\t');
            if (c.Length != 9)
            {
                rejects.Add("lt: short row");
                continue;
            }

            string number = c[2];
            string block = c[3];
            string settlement = c[7];
            string[] parts = c[8].Split(", ", StringSplitOptions.RemoveEmptyEntries);
            if (!LithuanianNumber().IsMatch(number) || parts.Length == 0 || !parts[0].EndsWith(" " + number, StringComparison.Ordinal))
            {
                rejects.Add("lt: no usable house number");
                continue;
            }

            string first = parts[0][..^(number.Length + 1)];
            string postal = string.Empty;
            string locality = string.Empty;
            foreach (string p in parts[1..])
            {
                if (p.StartsWith("LT-", StringComparison.Ordinal) && p.Length >= 8)
                {
                    postal = p[3..8];
                    locality = p.Length > 9 ? p[9..] : string.Empty;
                }
                else if (!p.StartsWith("korpusas ", StringComparison.Ordinal))
                {
                    locality = p;
                }
            }

            if (postal.Length != 5 || !postal.All(char.IsAsciiDigit))
            {
                rejects.Add("lt: no post code");
                continue;
            }

            bool streetless = c[1].Length == 0;
            if (streetless)
            {
                locality = first;
            }

            if (locality.Length == 0)
            {
                rejects.Add("lt: no locality");
                continue;
            }

            string unit = string.Empty;
            if (flats.TryGetValue(c[0], out List<string>? list))
            {
                unit = list[(int)(Prng.Hash("flat:" + c[0], 0) % (ulong)list.Count)];
            }

            bool village = settlement.EndsWith(" k.", StringComparison.Ordinal) || settlement.EndsWith(" vs.", StringComparison.Ordinal);
            yield return new Addr
            {
                Id = "adr:" + c[0],
                Country = "LT",
                Region = c[5],
                Language = "lt",
                Street = first,
                StreetType = streetless ? "NOSTREET" : "STREET",
                HouseNumber = number,
                Suffix = block.Length > 0 && block.All(char.IsAsciiLetterOrDigit) ? block : string.Empty,
                Unit = unit,
                Locality = locality,
                Dependent = village && !streetless ? settlement : string.Empty,
                Admin = c[5],
                PostalCode = postal,
            };
        }
    }
}
