using System.Buffers.Binary;
using System.Text;
using RecursiveAI.OneContact.Data;
using RecursiveAI.OneContact.Names;

namespace RecursiveAI.OneContact.Companies;

/// <summary>One row of the ELF code list: a legal form in one language of one jurisdiction.</summary>
internal sealed class ElfRow
{
    internal required string Code { get; init; }

    internal required string Country { get; init; }

    internal required string Subdivision { get; init; }

    internal required string Name { get; init; }

    internal required string Language { get; init; }

    internal required string TransliteratedName { get; init; }

    internal required string[] Abbreviations { get; init; }

    internal required string[] TransliteratedAbbreviations { get; init; }

    internal required bool IsActive { get; init; }
}

/// <summary>
/// The compiled ELF code list (table <c>forms</c> of the <c>gleif-elf</c> data set, written in docs/company-names.md), indexed by the
/// folded text of every name and abbreviation so a written legal form is found in one lookup.
/// </summary>
internal sealed class ElfTable
{
    private static readonly Lazy<ElfTable> Shared = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    private ElfTable(ElfRow[] rows, Dictionary<string, int[]> variants, Dictionary<string, int[]> abbreviations, Dictionary<string, int[]> byCode)
    {
        Rows = rows;
        Variants = variants;
        Abbreviations = abbreviations;
        ByCode = byCode;
    }

    internal static ElfTable Instance => Shared.Value;

    internal ElfRow[] Rows { get; }

    // Folded variant (letters and digits only) to the rows that list it.
    internal Dictionary<string, int[]> Variants { get; }

    // The same, for abbreviations only (and our aliases): a form written before the name must be an abbreviation, never a common word.
    internal Dictionary<string, int[]> Abbreviations { get; }

    // ELF code to its rows (one per language or subdivision).
    internal Dictionary<string, int[]> ByCode { get; }

    /// <summary>The longest variant, in folded letters, so matching stops looking at long tails.</summary>
    internal int MaxVariantLength { get; private init; }

    private static ElfTable Load()
    {
        EmbeddedTables tables = new(typeof(ElfTable).Assembly, "OneContact.Data.gleif-elf.ocd");
        return Parse(tables.GetTable("forms").Span);
    }

    internal static ElfTable Parse(ReadOnlySpan<byte> table)
    {
        int pos = 0;
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(table);
        pos += 4;
        if (count > 100_000)
        {
            throw new InvalidDataException("The ELF table's row count is implausible.");
        }

        ElfRow[] rows = new ElfRow[count];
        for (int i = 0; i < rows.Length; i++)
        {
            bool active = table[pos++] == 1;
            string code = ReadString(table, ref pos);
            string country = ReadString(table, ref pos);
            string subdivision = ReadString(table, ref pos);
            string name = ReadString(table, ref pos);
            string language = ReadString(table, ref pos);
            string translit = ReadString(table, ref pos);
            string abbreviations = ReadString(table, ref pos);
            string translitAbbreviations = ReadString(table, ref pos);
            rows[i] = new ElfRow
            {
                Code = code,
                Country = country,
                Subdivision = subdivision,
                Name = name,
                Language = language,
                TransliteratedName = translit,
                Abbreviations = Split(abbreviations),
                TransliteratedAbbreviations = Split(translitAbbreviations),
                IsActive = active,
            };
        }

        Dictionary<string, List<int>> variants = new(StringComparer.Ordinal);
        Dictionary<string, List<int>> abbrevIndex = new(StringComparer.Ordinal);
        Dictionary<string, List<int>> byCode = new(StringComparer.Ordinal);
        int max = 0;
        for (int i = 0; i < rows.Length; i++)
        {
            ElfRow row = rows[i];
            Add(byCode, row.Code, i);
            foreach (string v in AllVariants(row))
            {
                string folded = NameFolding.LettersAndDigits(v);

                // One-letter variants ("S", "A") would match every initial; CJK single characters are outside the Latin-script tiers.
                if (folded.Length < 2)
                {
                    continue;
                }

                max = Math.Max(max, folded.Length);
                Add(variants, folded, i);
                if (Array.IndexOf(row.Abbreviations, v) >= 0 || Array.IndexOf(row.TransliteratedAbbreviations, v) >= 0)
                {
                    Add(abbrevIndex, folded, i);
                }
            }
        }

        // Spellings people use that the list does not carry, tied to the listed form they stand for.
        Dictionary<string, List<int>> byName = new(StringComparer.Ordinal);
        for (int i = 0; i < rows.Length; i++)
        {
            Add(byName, rows[i].Country + "|" + NameFolding.LettersAndDigits(rows[i].Name), i);
        }

        foreach ((string country, string alias, string name) in ElfAliases.All)
        {
            if (!byName.TryGetValue(country + "|" + NameFolding.LettersAndDigits(name), out List<int>? target))
            {
                continue;
            }

            string folded = NameFolding.LettersAndDigits(alias);
            max = Math.Max(max, folded.Length);
            foreach (int row in target)
            {
                Add(variants, folded, row);
                Add(abbrevIndex, folded, row);
            }
        }

        return new ElfTable(rows, Freeze(variants), Freeze(abbrevIndex), Freeze(byCode)) { MaxVariantLength = max };
    }

    internal static IEnumerable<string> AllVariants(ElfRow row)
    {
        yield return row.Name;
        if (row.TransliteratedName.Length > 0)
        {
            yield return row.TransliteratedName;
        }

        foreach (string a in row.Abbreviations)
        {
            yield return a;
        }

        foreach (string a in row.TransliteratedAbbreviations)
        {
            yield return a;
        }
    }

    private static void Add(Dictionary<string, List<int>> map, string key, int row)
    {
        if (!map.TryGetValue(key, out List<int>? list))
        {
            list = [];
            map[key] = list;
        }

        if (list.Count == 0 || list[^1] != row)
        {
            list.Add(row);
        }
    }

    private static Dictionary<string, int[]> Freeze(Dictionary<string, List<int>> map)
    {
        Dictionary<string, int[]> frozen = new(map.Count, StringComparer.Ordinal);
        foreach (KeyValuePair<string, List<int>> pair in map)
        {
            frozen[pair.Key] = [.. pair.Value];
        }

        return frozen;
    }

    private static string[] Split(string text)
    {
        if (text.Length == 0)
        {
            return [];
        }

        List<string> parts = [];
        foreach (string part in text.Split(';'))
        {
            string trimmed = part.Trim();
            if (trimmed.Length > 0)
            {
                parts.Add(trimmed);
            }
        }

        return [.. parts];
    }

    private static string ReadString(ReadOnlySpan<byte> table, ref int pos)
    {
        if (pos + 2 > table.Length)
        {
            throw new InvalidDataException("The ELF table is truncated.");
        }

        int length = BinaryPrimitives.ReadUInt16LittleEndian(table[pos..]);
        pos += 2;
        if (pos + length > table.Length)
        {
            throw new InvalidDataException("The ELF table is truncated.");
        }

        string s = Encoding.UTF8.GetString(table.Slice(pos, length));
        pos += length;
        return s;
    }
}
