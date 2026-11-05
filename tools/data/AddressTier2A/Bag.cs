using System.IO.Compression;
using System.Text;

namespace OneContact.Data.AddressTier2A;

/// <summary>
/// The Dutch BAG extract (Kadaster, CC0), three members: addresses (NUM), public spaces (OPR) and places (WPL). An address names its public
/// space (ligtAan), a public space names its place (ligtIn); an address may name its own place when the street spans several. Only
/// objects whose status is "Naamgeving uitgegeven" (names in force) are used. The members are XML files with one object per element, read
/// here by element name; the extract is 17 GB unpacked, so nothing is parsed into a tree.
/// </summary>
internal static class Bag
{
    private const string InForce = "Naamgeving uitgegeven";

    internal static IEnumerable<Addr> Read(string dir, Rejects rejects)
    {
        string Member(string kind) => Directory.GetFiles(dir, "9999" + kind + "*.zip").Single();
        Dictionary<string, string> places = new(StringComparer.Ordinal);
        foreach (string obj in Objects(Member("WPL"), "Woonplaats"))
        {
            string id = Tag(obj, "Objecten:identificatie");
            string name = Tag(obj, "Objecten:naam");
            if (id.Length > 0 && name.Length > 0)
            {
                places[id] = Unescape(name);
            }
        }

        Dictionary<string, (string Name, string Place)> streets = new(StringComparer.Ordinal);
        foreach (string obj in Objects(Member("OPR"), "OpenbareRuimte"))
        {
            if (Tag(obj, "Objecten:status") != InForce)
            {
                continue;
            }

            streets[Tag(obj, "Objecten:identificatie")] = (Unescape(Tag(obj, "Objecten:naam")), Tag(obj, "Objecten-ref:WoonplaatsRef"));
        }

        foreach (string obj in Objects(Member("NUM"), "Nummeraanduiding"))
        {
            if (Tag(obj, "Objecten:status") != InForce)
            {
                rejects.Add("nl: status is not Naamgeving uitgegeven");
                continue;
            }

            string number = Tag(obj, "Objecten:huisnummer");
            string postal = Tag(obj, "Objecten:postcode");
            if (number.Length == 0 || postal.Length != 6 || !streets.TryGetValue(Tag(obj, "Objecten-ref:OpenbareRuimteRef"), out (string Name, string Place) street) || street.Name.Length == 0)
            {
                rejects.Add("nl: missing number, postcode or street");
                continue;
            }

            string placeId = Tag(obj, "Objecten-ref:WoonplaatsRef");
            if (placeId.Length == 0)
            {
                placeId = street.Place;
            }

            if (!places.TryGetValue(placeId, out string? place))
            {
                rejects.Add("nl: no place");
                continue;
            }

            string letter = Tag(obj, "Objecten:huisletter");
            string extra = Tag(obj, "Objecten:huisnummertoevoeging");
            string rep = letter + (extra.Length > 0 ? (letter.Length > 0 ? "-" : string.Empty) + extra : string.Empty);
            yield return new Addr
            {
                Id = "bag:" + Tag(obj, "Objecten:identificatie"),
                Country = "NL",
                Region = postal[..2],
                Language = "nl",
                Street = street.Name,
                HouseNumber = rep.Length == 0 ? number : number + "\u0001" + rep,
                Locality = place,
                PostalCode = postal,
            };
        }
    }

    /// <summary>The text of every <c>&lt;Objecten:kind&gt;...&lt;/Objecten:kind&gt;</c> element in the members of one zip.</summary>
    private static IEnumerable<string> Objects(string zipPath, string kind)
    {
        string open = "<Objecten:" + kind + ">";
        string close = "</Objecten:" + kind + ">";
        using ZipArchive zip = ZipFile.OpenRead(zipPath);
        foreach (ZipArchiveEntry entry in zip.Entries.OrderBy(e => e.FullName, StringComparer.Ordinal))
        {
            using StreamReader sr = new(entry.Open(), new UTF8Encoding(false), false, 1 << 20);
            string text = sr.ReadToEnd();
            int i = 0;
            while ((i = text.IndexOf(open, i, StringComparison.Ordinal)) >= 0)
            {
                int j = text.IndexOf(close, i, StringComparison.Ordinal);
                if (j < 0)
                {
                    break;
                }

                yield return text.Substring(i, j - i);
                i = j + close.Length;
            }
        }
    }

    // The content of the first <name ...>content</name> element of the object, or empty.
    private static string Tag(string obj, string name)
    {
        int i = obj.IndexOf("<" + name, StringComparison.Ordinal);
        while (i >= 0)
        {
            int after = i + 1 + name.Length;
            if (after < obj.Length && (obj[after] == '>' || obj[after] == ' '))
            {
                int start = obj.IndexOf('>', after) + 1;
                int end = obj.IndexOf("</" + name + ">", start, StringComparison.Ordinal);
                return start > 0 && end >= start ? obj[start..end] : string.Empty;
            }

            i = obj.IndexOf("<" + name, after, StringComparison.Ordinal);
        }

        return string.Empty;
    }

    private static string Unescape(string s) => s.Replace("&amp;", "&", StringComparison.Ordinal).Replace("&apos;", "'", StringComparison.Ordinal).Replace("&quot;", "\"", StringComparison.Ordinal)
        .Replace("&lt;", "<", StringComparison.Ordinal).Replace("&gt;", ">", StringComparison.Ordinal);
}
