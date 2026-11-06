using OneContact.Data.AddressTier2A;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml;

namespace OneContact.Data.AddressTier2C;

internal static partial class Derivation
{
    [GeneratedRegex(@"^(?<number>\d{1,4})(?: ?(?<letter>\p{L}{1,2}))?$", RegexOptions.CultureInvariant)]
    private static partial Regex SlovenianNumber();

    [GeneratedRegex(@"typeNames=ad:(?<type>\w+).*featureid=(?<id>[^&]*)$", RegexOptions.CultureInvariant)]
    private static partial Regex ComponentLink();

    private const string XLink = "http://www.w3.org/1999/xlink";

    // Register prostorskih enot (GURS, CC BY 4.0) through its INSPIRE addresses WFS, saved by fetch-si-rpe.py. An ad:Address carries the house
    // number (locator designator) and references to its components: the municipality (AdminUnitName), the settlement (AddressAreaName), the
    // street (ThoroughfareName; empty title in a village without street names) and the postal descriptor, whose own feature gives the post
    // code and the post office name. The post office name is what the last line of a Slovenian address carries.
    private static IEnumerable<string[]> Slovenia(string dir, Rejects rejects)
    {
        Dictionary<string, (string Code, string Name)> postal = [];
        using (Stream ps = new GZipStream(File.OpenRead(Path.Combine(dir, "ad-postal.xml.gz")), CompressionMode.Decompress))
        {
            foreach (string?[] v in XmlRecords.Read(ps, "PostalDescriptor", "localId", "postCode", "text"))
            {
                if (v[0] is not null && v[1] is not null && v[2] is not null)
                {
                    postal[v[0]!] = (v[1]!, v[2]!);
                }
            }
        }

        Console.Error.WriteLine($"si: {postal.Count} postal descriptors");
        using Stream s = new GZipStream(File.OpenRead(Path.Combine(dir, "ad-address.xml.gz")), CompressionMode.Decompress);
        XmlReaderSettings settings = new() { DtdProcessing = DtdProcessing.Prohibit, ConformanceLevel = ConformanceLevel.Fragment, IgnoreWhitespace = true };
        using XmlReader reader = XmlReader.Create(s, settings);
        bool more = reader.Read();
        while (more)
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "Address")
            {
                more = reader.Read();
                continue;
            }

            string localId = string.Empty;
            string designator = string.Empty;
            string municipality = string.Empty;
            string settlement = string.Empty;
            string street = string.Empty;
            string postalId = string.Empty;
            bool ended = false;
            int adminUnits = 0;
            using (XmlReader sub = reader.ReadSubtree())
            {
                sub.Read();
                sub.Read();
                while (!sub.EOF)
                {
                    if (sub.NodeType == XmlNodeType.Element)
                    {
                        string name = sub.LocalName;
                        if (name == "component")
                        {
                            string href = sub.GetAttribute("href", XLink) ?? string.Empty;
                            string title = sub.GetAttribute("title", XLink) ?? string.Empty;
                            Match m = ComponentLink().Match(href);
                            string type = m.Groups["type"].Value;
                            string id = m.Groups["id"].Value;
                            switch (type)
                            {
                                case "AdminUnitName" when ++adminUnits == 2:
                                    municipality = title;
                                    break;
                                case "AddressAreaName":
                                    settlement = title;
                                    break;
                                case "ThoroughfareName":
                                    street = title;
                                    break;
                                case "PostalDescriptor":
                                    postalId = id[(id.LastIndexOf('.') + 1)..];
                                    break;
                            }
                        }
                        else if (name == "endLifespanVersion")
                        {
                            ended = true;
                        }
                        else if ((name == "localId" && localId.Length == 0) || (name == "designator" && designator.Length == 0))
                        {
                            if (!sub.IsEmptyElement)
                            {
                                sub.Read();
                                if (sub.NodeType == XmlNodeType.Text)
                                {
                                    if (name == "localId")
                                    {
                                        localId = sub.Value;
                                    }
                                    else
                                    {
                                        designator = sub.Value;
                                    }
                                }

                                continue;
                            }
                        }
                    }

                    sub.Read();
                }
            }

            more = reader.Read();
            if (ended)
            {
                rejects.Add("si: address no longer valid");
                continue;
            }

            Match number = SlovenianNumber().Match(designator.Trim());
            if (!number.Success || localId.Length == 0)
            {
                rejects.Add("si: no usable house number");
                continue;
            }

            if (!postal.TryGetValue(postalId, out (string Code, string Name) post) || post.Code.Length != 4 || municipality.Length == 0 || settlement.Length == 0)
            {
                rejects.Add("si: no post code, municipality or settlement");
                continue;
            }

            bool hasStreet = street.Length > 0;
            yield return Row("rpe:" + localId, municipality, hasStreet ? street : settlement, hasStreet ? "STREET" : "NOSTREET", number.Groups["number"].Value, number.Groups["letter"].Value, string.Empty, string.Empty, post.Name,
                hasStreet && settlement != post.Name ? settlement : string.Empty, string.Empty, post.Code);
        }
    }
}
