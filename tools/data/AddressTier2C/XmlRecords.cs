using System.IO.Compression;
using System.Xml;

namespace OneContact.Data.AddressTier2C;

internal static class XmlRecords
{
    /// <summary>
    /// Streams the elements called <paramref name="record"/> out of an XML document (or a fragment of several documents' root elements)
    /// and yields, for each, the text of the first element of each wanted local name inside it, in the order of <paramref name="wanted"/>
    /// (null where absent). The array is reused: copy what is kept.
    /// </summary>
    internal static IEnumerable<string?[]> Read(Stream stream, string record, params string[] wanted)
    {
        XmlReaderSettings settings = new() { DtdProcessing = DtdProcessing.Prohibit, ConformanceLevel = ConformanceLevel.Fragment, IgnoreWhitespace = true, CloseInput = false };
        using XmlReader reader = XmlReader.Create(stream, settings);
        Dictionary<string, int> index = new(StringComparer.Ordinal);
        for (int i = 0; i < wanted.Length; i++)
        {
            index[wanted[i]] = i;
        }

        string?[] values = new string?[wanted.Length];
        bool more = reader.Read();
        while (more)
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != record)
            {
                more = reader.Read();
                continue;
            }

            Array.Clear(values);
            using (XmlReader sub = reader.ReadSubtree())
            {
                sub.Read();
                sub.Read();
                while (!sub.EOF)
                {
                    if (sub.NodeType == XmlNodeType.Element && index.TryGetValue(sub.LocalName, out int i))
                    {
                        if (sub.IsEmptyElement)
                        {
                            sub.Read();
                            continue;
                        }

                        sub.Read();
                        if (sub.NodeType == XmlNodeType.Text && values[i] is null)
                        {
                            values[i] = sub.Value;
                        }

                        continue;
                    }

                    sub.Read();
                }
            }

            yield return values;
            more = reader.Read();
        }
    }

    /// <summary>The unzipped single entry of an archive as a stream (the first entry, or the first whose name ends as given).</summary>
    internal static Stream FirstEntry(string zipPath, out ZipArchive archive)
    {
        archive = ZipFile.OpenRead(zipPath);
        return archive.Entries[0].Open();
    }

    /// <summary>True when a validity end date is far in the future, which is how the Slovak register marks the version in force (3000-12-31).</summary>
    internal static bool InForce(string? validTo) => validTo is { Length: >= 4 } && int.TryParse(validTo.AsSpan(0, 4), out int year) && year >= 2100;
}
