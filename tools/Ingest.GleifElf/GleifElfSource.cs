using System.Buffers.Binary;
using System.Text;

namespace RecursiveAI.OneContact.Ingest.GleifElf;

/// <summary>
/// Converts the GLEIF ISO 20275 entity legal form code list (CSV, one row per code, language and jurisdiction) into the table
/// <c>forms</c> and the text table <c>version</c>. The layout of <c>forms</c> is written in docs/company-names.md.
/// </summary>
public sealed class GleifElfSource : IIngestSource
{
    private const string FileName = "2026-02-19-elf-code-list-v1.6.csv";

    // Column headers, as published; a changed header means a changed format and must stop the build rather than shift columns.
    private static readonly string[] Headers =
    [
        "ELF Code",
        "Country of formation",
        "Country Code (ISO 3166-1)",
        "Jurisdiction of formation",
        "Country sub-division code (ISO 3166-2)",
        "Entity Legal Form name Local name",
        "Language",
        "Language Code (ISO 639-1)",
        "Entity Legal Form name Transliterated name (per ISO 01-140-10)",
        "Abbreviations Local language",
        "Abbreviations transliterated",
        "Date created YYYY-MM-DD (ISO 8601)",
        "ELF Status ACTV/INAC",
        "Modification",
        "Modification date YYYY-MM-DD (ISO 8601)",
        "Reason",
    ];

    // The columns kept, in the order they are written after the status byte.
    private static readonly int[] KeptColumns = [0, 2, 4, 5, 7, 8, 9, 10];

    /// <inheritdoc/>
    public string Name => "gleif-elf";

    /// <inheritdoc/>
    public string ToolVersion => "1";

    /// <inheritdoc/>
    public string NoticeText =>
        "Entity Legal Forms (ELF) code list, ISO 20275, version 1.6 (2026-02-19), published by the Global Legal Entity Identifier Foundation (GLEIF).\n" +
        "Dedicated to the public domain under CC0 1.0 Universal (https://creativecommons.org/publicdomain/zero/1.0/).\n" +
        "Credit: GLEIF, https://www.gleif.org/en/lei-data/code-lists/iso-20275-entity-legal-forms-code-list\n";

    /// <inheritdoc/>
    public void Convert(IngestInput input, TableSetWriter tables)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(tables);
        string text = new UTF8Encoding(false, true).GetString(input.ReadAllBytes(FileName));
        if (text.Length > 0 && text[0] == '\uFEFF')
        {
            text = text[1..];
        }

        List<string[]> rows = ReadCsv(text);
        if (rows.Count < 2 || !rows[0].AsSpan().SequenceEqual(Headers))
        {
            throw new InvalidDataException("The ELF code list does not have the expected columns.");
        }

        // Reserved codes 8888 and 9999 carry no country and no name: they are not legal forms.
        List<(string[] Row, int Index)> forms = [];
        for (int i = 1; i < rows.Count; i++)
        {
            string[] row = rows[i];
            if (row.Length != Headers.Length)
            {
                throw new InvalidDataException($"ELF row {i} has {row.Length} columns.");
            }

            if (row[2].Length == 0 || row[5].Length == 0)
            {
                continue;
            }

            forms.Add((row, i));
        }

        // Country, then code, then language, then file order: the same input always gives the same bytes.
        forms.Sort(static (a, b) =>
        {
            int c = string.CompareOrdinal(a.Row[2], b.Row[2]);
            if (c == 0)
            {
                c = string.CompareOrdinal(a.Row[0], b.Row[0]);
            }

            if (c == 0)
            {
                c = string.CompareOrdinal(a.Row[7], b.Row[7]);
            }

            return c != 0 ? c : a.Index.CompareTo(b.Index);
        });

        using MemoryStream stream = new();
        WriteUInt32(stream, (uint)forms.Count);
        foreach ((string[] row, _) in forms)
        {
            stream.WriteByte(row[12] == "ACTV" ? (byte)1 : (byte)0);
            foreach (int column in KeptColumns)
            {
                WriteString(stream, row[column]);
            }
        }

        tables.Add("forms", stream.ToArray());
        tables.Add("version", Encoding.UTF8.GetBytes("ISO 20275 ELF code list v1.6, 2026-02-19"));
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteString(Stream stream, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > ushort.MaxValue)
        {
            throw new InvalidDataException("A field is too long.");
        }

        Span<byte> length = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(length, (ushort)bytes.Length);
        stream.Write(length);
        stream.Write(bytes);
    }

    // RFC 4180: quoted fields may hold commas, doubled quotes and line breaks.
    private static List<string[]> ReadCsv(string text)
    {
        List<string[]> rows = [];
        List<string> fields = [];
        StringBuilder field = new();
        bool quoted = false;
        bool any = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    quoted = true;
                    any = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    any = true;
                    break;
                case '\r':
                    break;
                case '\n':
                    if (any || field.Length > 0)
                    {
                        fields.Add(field.ToString());
                        rows.Add([.. fields]);
                    }

                    fields.Clear();
                    field.Clear();
                    any = false;
                    break;
                default:
                    field.Append(c);
                    any = true;
                    break;
            }
        }

        if (quoted)
        {
            throw new InvalidDataException("The CSV ends inside a quoted field.");
        }

        if (any || field.Length > 0)
        {
            fields.Add(field.ToString());
            rows.Add([.. fields]);
        }

        return rows;
    }
}
