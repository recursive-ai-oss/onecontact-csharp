using System.IO.Compression;
using System.Text;
using System.Xml;

namespace OneContact.Data.Names;

/// <summary>
/// A minimal reader for the spreadsheets the statistics offices publish (Office Open XML): the text of the cells of one named sheet, row by row.
/// Cell types other than text and numbers are not needed by the name lists and are returned as written.
/// </summary>
internal static class Xlsx
{
    /// <summary>Yields the rows of the sheet <paramref name="sheetName"/> (or the first sheet when null) as arrays of cell text, columns A.. in order, empty cells as "".</summary>
    internal static IEnumerable<string[]> Rows(string path, string? sheetName = null)
    {
        using ZipArchive zip = ZipFile.OpenRead(path);

        List<string> shared = [];
        ZipArchiveEntry? sharedEntry = zip.GetEntry("xl/sharedStrings.xml");
        if (sharedEntry != null)
        {
            using Stream s = sharedEntry.Open();
            using XmlReader xr = XmlReader.Create(s, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
            StringBuilder sb = new();
            bool inItem = false;
            while (xr.Read())
            {
                if (xr.NodeType == XmlNodeType.Element && xr.LocalName == "si")
                {
                    inItem = true;
                    sb.Clear();
                    if (xr.IsEmptyElement)
                    {
                        shared.Add("");
                        inItem = false;
                    }
                }
                else if (xr.NodeType == XmlNodeType.Text && inItem)
                {
                    sb.Append(xr.Value);
                }
                else if (xr.NodeType == XmlNodeType.EndElement && xr.LocalName == "si")
                {
                    shared.Add(sb.ToString());
                    inItem = false;
                }
            }
        }

        // workbook.xml names the sheets; workbook.xml.rels maps their ids to part names.
        Dictionary<string, string> targets = new(StringComparer.Ordinal);
        using (Stream s = zip.GetEntry("xl/_rels/workbook.xml.rels")!.Open())
        using (XmlReader xr = XmlReader.Create(s, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit }))
        {
            while (xr.Read())
            {
                if (xr.NodeType == XmlNodeType.Element && xr.LocalName == "Relationship")
                {
                    targets[xr.GetAttribute("Id")!] = xr.GetAttribute("Target")!;
                }
            }
        }

        string? part = null;
        using (Stream s = zip.GetEntry("xl/workbook.xml")!.Open())
        using (XmlReader xr = XmlReader.Create(s, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit }))
        {
            while (xr.Read())
            {
                if (xr.NodeType == XmlNodeType.Element && xr.LocalName == "sheet")
                {
                    string name = xr.GetAttribute("name")!;
                    string id = xr.GetAttribute("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships")!;
                    if (sheetName == null || string.Equals(name, sheetName, StringComparison.Ordinal))
                    {
                        part = targets[id].TrimStart('/');
                        if (!part.StartsWith("xl/", StringComparison.Ordinal))
                        {
                            part = "xl/" + part;
                        }

                        break;
                    }
                }
            }
        }

        if (part == null)
        {
            throw new InvalidDataException($"{path}: no sheet {sheetName}");
        }

        using Stream sheet = zip.GetEntry(part)!.Open();
        using XmlReader sx = XmlReader.Create(sheet, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        List<string> row = [];
        while (sx.Read())
        {
            if (sx.NodeType == XmlNodeType.Element && sx.LocalName == "row")
            {
                row = [];
            }
            else if (sx.NodeType == XmlNodeType.Element && sx.LocalName == "c")
            {
                string r = sx.GetAttribute("r")!;
                string? type = sx.GetAttribute("t");
                int col = 0;
                foreach (char ch in r)
                {
                    if (ch is < 'A' or > 'Z')
                    {
                        break;
                    }

                    col = (col * 26) + (ch - 'A' + 1);
                }

                col--;
                string value = "";
                using (XmlReader cell = sx.ReadSubtree())
                {
                    cell.Read();
                    while (cell.Read())
                    {
                        if (cell.NodeType == XmlNodeType.Element && (cell.LocalName == "v" || cell.LocalName == "t"))
                        {
                            bool isV = cell.LocalName == "v";
                            string text = cell.ReadElementContentAsString();
                            value = type == "s" && isV ? shared[int.Parse(text, System.Globalization.CultureInfo.InvariantCulture)] : text;
                        }
                    }
                }

                while (row.Count < col)
                {
                    row.Add("");
                }

                if (row.Count == col)
                {
                    row.Add(value);
                }
                else
                {
                    row[col] = value;
                }
            }
            else if (sx.NodeType == XmlNodeType.EndElement && sx.LocalName == "row")
            {
                yield return row.ToArray();
            }
        }
    }
}
