using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using OneContact.Data.AddressTier2A;

namespace OneContact.Data.AddressTier2C;

/// <summary>Fields the group C-1 registers need beyond the shared address shape.</summary>
internal sealed partial record Addr
{
    /// <summary>The register's kind of thoroughfare in capitals ("AVENIDA", "CALLE"), when the register keeps it apart from the name.</summary>
    internal string StreetType { get; init; } = string.Empty;

    /// <summary>The register's kind of neighbourhood in capitals ("COLONIA", "FRACCIONAMIENTO").</summary>
    internal string PlaceType { get; init; } = string.Empty;

    /// <summary>A second number for the same entrance: the Slovak registration number written before the orientation number ("15/8").</summary>
    internal string AltNumber { get; init; } = string.Empty;
}

internal static partial class Registers
{
    private static readonly HashSet<string> SpanishParticles = new(StringComparer.Ordinal)
    {
        "de", "del", "la", "las", "los", "el", "y", "e", "en", "a", "al", "con", "por", "para",
    };

    [GeneratedRegex(@"^(?=[IVXL])(X{0,3})(IX|IV|V?I{0,3})$", RegexOptions.CultureInvariant)]
    private static partial Regex RomanNumeral();

    [GeneratedRegex(@"^\d+(ra|da|a|er|o|do|to|vo|no|mo)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex SpanishOrdinal();

    /// <summary>"MIGUEL HIDALGO Y COSTILLA" to "Miguel Hidalgo y Costilla": the register writes capitals, Mexican mail writes names in title case with the particles small.</summary>
    internal static string TitleCaseEs(string upper)
    {
        string[] words = upper.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        StringBuilder sb = new(upper.Length);
        for (int i = 0; i < words.Length; i++)
        {
            string w = words[i];
            string lower = w.ToLowerInvariant();
            if (sb.Length > 0)
            {
                sb.Append(' ');
            }

            if (i > 0 && SpanishParticles.Contains(lower))
            {
                sb.Append(lower);
            }
            else if (SpanishOrdinal().IsMatch(w) || (w.Length > 0 && char.IsDigit(w[0])))
            {
                sb.Append(lower);
            }
            else if (RomanNumeral().IsMatch(w))
            {
                sb.Append(w);
            }
            else
            {
                bool start = true;
                foreach (char c in lower)
                {
                    sb.Append(start ? char.ToUpperInvariant(c) : c);
                    start = c is '-' or '/' or '(';
                }
            }
        }

        return sb.ToString();
    }

    [GeneratedRegex(@"^\d{1,5}$", RegexOptions.CultureInvariant)]
    private static partial Regex PlainNumber();

    [GeneratedRegex(@"^[0-9A-Z]{1,5}$", RegexOptions.CultureInvariant)]
    private static partial Regex InteriorNumber();

    // INEGI DENUE, one zip per state (INEGI "Términos de Libre Uso"), Latin-1 text: an establishment directory, of which only the address
    // columns are read. tipo_vial / nom_vial are the thoroughfare kind and name, numero_ext / letra_ext the outside number (letra "SN" means
    // sin número), numero_int the interior number, tipo_asent / nomb_asent the neighbourhood, cod_postal the five-digit post code (the
    // register drops the leading zero of some), municipio the municipality and entidad the state. Many establishments share one address;
    // the renderer keeps one of each.
    private static IEnumerable<Addr> Mexico(string directory, Rejects rejects)
    {
        foreach (string file in System.IO.Directory.GetFiles(directory, "denue_*_csv.zip").OrderBy(f => f, StringComparer.Ordinal))
        {
            using ZipArchive zip = ZipFile.OpenRead(file);
            ZipArchiveEntry entry = zip.Entries.First(e => e.FullName.StartsWith("conjunto_de_datos/", StringComparison.Ordinal) && e.Name.EndsWith(".csv", StringComparison.Ordinal));
            using StreamReader sr = new(entry.Open(), Encoding.Latin1, false, 1 << 20);
            DelimitedTable t = new(sr, ',');
            while (t.Read())
            {
                string name = t["nom_vial"].Trim();
                if (name.Length == 0 || name.StartsWith("SIN NOMBRE", StringComparison.Ordinal) || name == "NINGUNO")
                {
                    rejects.Add("mx: no street name");
                    continue;
                }

                string number = t["numero_ext"].Trim();
                string letter = t["letra_ext"].Trim().ToUpperInvariant();
                string house;
                string suffix = string.Empty;
                if (PlainNumber().IsMatch(number) && number.Any(c => c != '0'))
                {
                    house = number;
                    suffix = letter.Length == 1 && char.IsAsciiLetterUpper(letter[0]) ? letter : string.Empty;
                }
                else if (number.Length == 0 && letter == "SN")
                {
                    house = "S/N";
                }
                else
                {
                    rejects.Add("mx: no usable outside number");
                    continue;
                }

                string postal = t["cod_postal"].Trim();
                if (postal.Length is < 4 or > 5 || !postal.All(char.IsAsciiDigit) || postal.All(c => c == '0'))
                {
                    rejects.Add("mx: no usable post code");
                    continue;
                }

                string municipality = t["municipio"].Trim();
                string state = t["entidad"].Trim();
                if (municipality.Length == 0 || state.Length == 0)
                {
                    rejects.Add("mx: no municipality or state");
                    continue;
                }

                string interior = (t["numero_int"].Trim() + t["letra_int"].Trim()).ToUpperInvariant();
                string neighbourhood = t["nomb_asent"].Trim();
                string placeType = t["tipo_asent"].Trim().ToUpperInvariant();
                if (neighbourhood.Length == 0 || neighbourhood == "NINGUNO" || placeType == "NINGUNO")
                {
                    neighbourhood = string.Empty;
                }

                yield return new Addr
                {
                    Id = "denue:" + t["id"],
                    Country = "MX",
                    Region = t["cve_ent"],
                    Language = "es",
                    StreetType = t["tipo_vial"].Trim().ToUpperInvariant(),
                    Street = TitleCaseEs(name),
                    HouseNumber = house,
                    Suffix = suffix,
                    Unit = InteriorNumber().IsMatch(interior) ? interior : string.Empty,
                    PlaceType = placeType,
                    Dependent = neighbourhood.Length == 0 ? string.Empty : TitleCaseEs(neighbourhood),
                    Locality = municipality,
                    Admin = state,
                    PostalCode = postal.PadLeft(5, '0'),
                };
            }
        }
    }
}
