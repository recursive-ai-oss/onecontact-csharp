using System.Text;

namespace OneContact.Data.Names;

/// <summary>
/// A streaming reader for delimiter-separated text with double-quote quoting (RFC 4180: quotes doubled inside a quoted field, line breaks allowed
/// inside quotes). It keeps only the columns the caller asks for, so a register with hundreds of columns costs one string per wanted column.
/// </summary>
internal sealed class CsvReader : IDisposable
{
    private readonly TextReader _reader;
    private readonly char _delimiter;
    private readonly StringBuilder _field = new();

    internal CsvReader(TextReader reader, char delimiter = ',')
    {
        _reader = reader;
        _delimiter = delimiter;
    }

    /// <summary>Reads one record. <paramref name="row"/>[i] is set for the columns with <paramref name="keep"/>[i] (null for the others); returns false at the end.</summary>
    internal bool Read(string?[] row, bool[] keep, out int count)
    {
        Array.Clear(row);
        count = 0;
        int c = _reader.Read();
        if (c < 0)
        {
            return false;
        }

        while (true)
        {
            bool store = count < keep.Length && keep[count];
            _field.Clear();
            bool quoted = false;
            if (c == '"')
            {
                quoted = true;
                c = _reader.Read();
                while (c >= 0)
                {
                    if (c == '"')
                    {
                        int n = _reader.Read();
                        if (n == '"')
                        {
                            if (store)
                            {
                                _field.Append('"');
                            }

                            c = _reader.Read();
                            continue;
                        }

                        c = n;
                        break;
                    }

                    if (store)
                    {
                        _field.Append((char)c);
                    }

                    c = _reader.Read();
                }
            }
            else
            {
                while (c >= 0 && c != _delimiter && c != '\n' && c != '\r')
                {
                    if (store)
                    {
                        _field.Append((char)c);
                    }

                    c = _reader.Read();
                }
            }

            while (quoted && c >= 0 && c != _delimiter && c != '\n' && c != '\r')
            {
                c = _reader.Read();
            }

            if (store)
            {
                row[count] = _field.ToString();
            }

            count++;
            if (c == _delimiter)
            {
                c = _reader.Read();
                continue;
            }

            if (c == '\r' && _reader.Peek() == '\n')
            {
                _reader.Read();
            }

            return true;
        }
    }

    public void Dispose() => _reader.Dispose();
}
