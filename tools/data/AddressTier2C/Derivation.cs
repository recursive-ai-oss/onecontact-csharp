using OneContact.Data.AddressTier2A;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace OneContact.Data.AddressTier2C;

/// <summary>
/// Reads a dBASE III table (the attribute file of a shapefile) record by record from a stream, UTF-8 text: the 32-byte header gives the
/// record count, header length and record length, then 32-byte field descriptors up to the 0x0D terminator, then fixed-width records
/// that start with a deletion flag.
/// </summary>
internal sealed class DbfReader
{
    private readonly Stream _stream;
    private readonly byte[] _record;
    private readonly Dictionary<string, (int Offset, int Length)> _fields = new(StringComparer.Ordinal);

    internal DbfReader(Stream stream)
    {
        _stream = stream;
        byte[] head = new byte[32];
        ReadExactly(head);
        RecordCount = BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(4));
        int headerLength = BinaryPrimitives.ReadUInt16LittleEndian(head.AsSpan(8));
        int recordLength = BinaryPrimitives.ReadUInt16LittleEndian(head.AsSpan(10));
        int offset = 1;
        byte[] descriptor = new byte[32];
        int read = 32;
        while (true)
        {
            ReadExactly(descriptor.AsSpan(0, 1));
            read++;
            if (descriptor[0] == 0x0D)
            {
                break;
            }

            ReadExactly(descriptor.AsSpan(1, 31));
            read += 31;
            string name = Encoding.ASCII.GetString(descriptor, 0, 11).TrimEnd('\0');
            int length = descriptor[16];
            _fields[name] = (offset, length);
            offset += length;
        }

        // Anything between the descriptors and the first record (some writers pad the header).
        byte[] skip = new byte[Math.Max(0, headerLength - read)];
        ReadExactly(skip);
        _record = new byte[recordLength];
    }

    internal uint RecordCount { get; }

    /// <summary>Advances to the next record; false at the end. Deleted records are skipped.</summary>
    internal bool Read()
    {
        while (true)
        {
            int got = 0;
            while (got < _record.Length)
            {
                int n = _stream.Read(_record, got, _record.Length - got);
                if (n == 0)
                {
                    return false;
                }

                got += n;
            }

            if (_record[0] == 0x1A)
            {
                return false;
            }

            if (_record[0] != 0x2A)
            {
                return true;
            }
        }
    }

    internal string this[string field] => _fields.TryGetValue(field, out (int Offset, int Length) f) ? Encoding.UTF8.GetString(_record, f.Offset, f.Length).Trim().Trim('\0') : string.Empty;

    private void ReadExactly(Span<byte> buffer)
    {
        _stream.ReadExactly(buffer);
    }
}

/// <summary>
/// One-off conversions of registers that are too large or too awkward to read twice into the plain table the renderer reads
/// (derived/&lt;cc&gt;-addresses.tsv.gz next to the pinned files): tab separated, one address per line, columns
/// id, region, street, street type, house number, suffix, unit, second number, locality, dependent locality, admin area, post code.
/// </summary>
internal static partial class Derivation
{
    internal const string Header = "id\tregion\tstreet\tstreettype\thouse\tsuffix\tunit\talt\tlocality\tdependent\tadmin\tpostal";

    internal static int Run(Dictionary<string, string> o)
    {
        string data = o["data"];
        Rejects rejects = new();
        foreach (string country in o["countries"].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(c => c.Trim().ToUpperInvariant()))
        {
            string prefix = country.ToLowerInvariant() switch { "pl" => "pl-prg", "sk" => "sk-ra", "si" => "si-rpe", _ => throw new ArgumentException("no conversion for " + country) };
            Func<string, Rejects, IEnumerable<string[]>> read = country switch { "PL" => Poland, "SK" => Slovakia, _ => Slovenia };

            string dir = Registers.Directory(data, prefix);
            string derived = Path.Combine(dir, "derived");
            System.IO.Directory.CreateDirectory(derived);
            string output = Path.Combine(derived, country.ToLowerInvariant() + "-addresses.tsv.gz");
            long rows = 0;
            using (FileStream fs = File.Create(output))
            using (GZipStream gz = new(fs, CompressionLevel.Optimal))
            using (StreamWriter w = new(gz, new UTF8Encoding(false)) { NewLine = "\n" })
            {
                w.WriteLine(Header);
                foreach (string[] row in read(dir, rejects))
                {
                    w.WriteLine(string.Join('\t', row));
                    rows++;
                }
            }

            Console.WriteLine($"derived {Path.GetFileName(output)} {new FileInfo(output).Length} {Renderer.Sha256Of(output)} rows={rows}");
        }

        foreach (KeyValuePair<string, long> kv in rejects.Counts)
        {
            Console.WriteLine($"rejected\t{kv.Key}\t{kv.Value}");
        }

        return 0;
    }

    private static string Clean(string s) => string.Join(' ', s.Replace('\t', ' ').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string[] Row(string id, string region, string street, string streetType, string house, string suffix, string unit, string alt, string locality, string dependent, string admin, string postal) =>
        [id, region, Clean(street), streetType, Clean(house), Clean(suffix), Clean(unit), Clean(alt), Clean(locality), Clean(dependent), Clean(admin), postal];

    [GeneratedRegex(@"^\d{1,4}[A-Za-z]?(?:[/-]\d{1,4}[A-Za-z]?)?$", RegexOptions.CultureInvariant)]
    private static partial Regex PolishNumber();

    // PRG address points (GUGiK), PRG_PunktyAdresowe_POLSKA.dbf in POLSKA.zip: NUMER_PORZ the house number, KOD_POCZT the post code "97-300",
    // NAZWA_ULC the street (empty in a village without streets), NAZWA_MSC the locality, TERYT_GMI the municipality code whose first two digits
    // are the voivodeship. The register does not give the post office town; the locality stands in for it.
    private static IEnumerable<string[]> Poland(string dir, Rejects rejects)
    {
        using ZipArchive zip = ZipFile.OpenRead(Path.Combine(dir, "POLSKA.zip"));
        ZipArchiveEntry entry = zip.GetEntry("PRG_PunktyAdresowe_POLSKA.dbf") ?? throw new InvalidDataException("no address points in the archive");
        using Stream s = entry.Open();
        DbfReader dbf = new(new BufferedStream(s, 1 << 20));
        while (dbf.Read())
        {
            string number = dbf["NUMER_PORZ"];
            string postal = dbf["KOD_POCZT"];
            string locality = dbf["NAZWA_MSC"];
            string teryt = dbf["TERYT_GMI"];
            string id = dbf["ID_IIP"];
            int u = id.IndexOf('_', StringComparison.Ordinal);
            int u2 = id.LastIndexOf('_');
            id = u >= 0 && u2 > u ? id[(u + 1)..u2] : id;
            if (!PolishNumber().IsMatch(number) || number.All(c => c is '0' or '/' or '-'))
            {
                rejects.Add("pl: no usable house number");
                continue;
            }

            if (postal.Length != 6 || postal[2] != '-' || !postal.Where(c => c != '-').All(char.IsAsciiDigit))
            {
                rejects.Add("pl: no usable post code");
                continue;
            }

            if (locality.Length == 0 || teryt.Length < 2)
            {
                rejects.Add("pl: no locality or municipality code");
                continue;
            }

            string street = dbf["NAZWA_ULC"];
            yield return Row("prg:" + id, teryt[..2], street.Length == 0 ? locality : street, street.Length == 0 ? "NOSTREET" : "STREET", number, string.Empty, string.Empty, string.Empty, locality, string.Empty, string.Empty, postal.Replace("-", string.Empty, StringComparison.Ordinal));
        }
    }
}
