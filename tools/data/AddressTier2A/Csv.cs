using System.Text;

namespace OneContact.Data.AddressTier2A;

/// <summary>Why a register row did not become a usable record; the counts go in the manifest.</summary>
internal sealed class Rejects
{
    internal SortedDictionary<string, long> Counts { get; } = new(StringComparer.Ordinal);

    internal void Add(string reason) => Counts[reason] = Counts.GetValueOrDefault(reason) + 1;
}

/// <summary>
/// Reads a delimited text file with a header row (RFC 4180 quoting, a quoted field may hold the delimiter and doubled quotes); fields are
/// reached by column name. A UTF-8 byte-order mark in front of the first column name is dropped.
/// </summary>
internal sealed class DelimitedTable
{
    private readonly TextReader _reader;
    private readonly char _delimiter;
    private readonly Dictionary<string, int> _columns = new(StringComparer.Ordinal);
    private readonly List<string> _fields = [];

    internal DelimitedTable(TextReader reader, char delimiter)
    {
        _reader = reader;
        _delimiter = delimiter;
        List<string> names = [];
        if (!ReadRecord(names))
        {
            throw new InvalidDataException("empty file");
        }

        for (int i = 0; i < names.Count; i++)
        {
            _columns[names[i].Trim('\uFEFF', '"')] = i;
        }
    }

    internal bool Read() => ReadRecord(_fields);

    /// <summary>The value of the named column in the current row, or empty when the row is short.</summary>
    internal string this[string column] => _columns.TryGetValue(column, out int i) && i < _fields.Count ? _fields[i] : string.Empty;

    /// <summary>The value at a column position (files with no usable header), or empty.</summary>
    internal string At(int i) => i < _fields.Count ? _fields[i] : string.Empty;

    private bool ReadRecord(List<string> into)
    {
        into.Clear();
        string? line = _reader.ReadLine();
        if (line is null)
        {
            return false;
        }

        StringBuilder sb = new();
        bool quoted = false;
        while (true)
        {
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            sb.Append('"');
                            i++;
                        }
                        else
                        {
                            quoted = false;
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                else if (c == '"' && sb.Length == 0)
                {
                    quoted = true;
                }
                else if (c == _delimiter)
                {
                    into.Add(sb.ToString());
                    sb.Clear();
                }
                else
                {
                    sb.Append(c);
                }
            }

            if (!quoted)
            {
                break;
            }

            // A quoted field that continues on the next line.
            sb.Append('\n');
            line = _reader.ReadLine();
            if (line is null)
            {
                break;
            }
        }

        into.Add(sb.ToString());
        return true;
    }
}
