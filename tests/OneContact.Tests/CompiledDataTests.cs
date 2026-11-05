using System.Buffers.Binary;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using RecursiveAI.OneContact.Data;
using RecursiveAI.OneContact.Ingest;
using RecursiveAI.OneContact.Ingest.Synthetic;

namespace RecursiveAI.OneContact.Tests;

public sealed class CompiledDataTests : IDisposable
{
    private static readonly string Fixture = Path.Combine(AppContext.BaseDirectory, "testdata", "ingest-synthetic");
    private readonly string _work = Path.Combine(Path.GetTempPath(), "onecontact-tests-" + Guid.NewGuid().ToString("N"));

    public CompiledDataTests() => Directory.CreateDirectory(_work);

    public void Dispose() => Directory.Delete(_work, recursive: true);

    private static PinFile Pin() => PinFile.Load(Path.Combine(Fixture, "PIN"));

    private static string Files => Path.Combine(Fixture, "files");

    // --- the embedding path: built by the tool at build time, embedded in this assembly, loaded lazily ---

    [Fact]
    public void Embedded_tables_are_loaded_on_first_use_and_hold_what_the_tool_wrote()
    {
        EmbeddedTables tables = new(typeof(CompiledDataTests).Assembly, "OneContact.Data.synthetic.ocd");
        Assert.False(tables.IsLoaded);

        Assert.Equal("red\ngreen\nblue", Encoding.UTF8.GetString(tables.GetTable("colors").Span));
        Assert.True(tables.IsLoaded);
        ReadOnlySpan<byte> numbers = tables.GetTable("numbers").Span;
        Assert.Equal(20, numbers.Length);
        Assert.Equal(4, BinaryPrimitives.ReadInt32LittleEndian(numbers[8..]));
        Assert.Throws<InvalidDataException>(() => tables.GetTable("missing"));
    }

    [Fact]
    public void A_missing_resource_fails_when_first_used_not_when_described()
    {
        EmbeddedTables tables = new(typeof(CompiledDataTests).Assembly, "OneContact.Data.nothing.ocd");
        Assert.Throws<InvalidDataException>(() => tables.GetTable("colors"));
    }

    // --- the ingest harness ---

    [Fact]
    public void Ingest_writes_tables_provenance_and_notice_and_is_deterministic()
    {
        IngestOutput first = IngestRunner.Run(Pin(), Files, Path.Combine(_work, "a"), new SyntheticSource());
        IngestOutput second = IngestRunner.Run(Pin(), Files, Path.Combine(_work, "b"), new SyntheticSource());

        Assert.Equal(File.ReadAllBytes(first.TablesPath), File.ReadAllBytes(second.TablesPath));
        Assert.Equal(File.ReadAllBytes(first.ProvenancePath), File.ReadAllBytes(second.ProvenancePath));
        Assert.Equal(File.ReadAllBytes(first.NoticePath), File.ReadAllBytes(second.NoticePath));

        string provenance = File.ReadAllText(first.ProvenancePath);
        Assert.Contains("\"source\": \"synthetic\"", provenance, StringComparison.Ordinal);
        Assert.Contains("\"ref\": \"v1.0.0\"", provenance, StringComparison.Ordinal);
        foreach (PinnedFile f in Pin().Files)
        {
            Assert.Contains(f.Sha256, provenance, StringComparison.Ordinal);
        }

        string tablesHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(first.TablesPath)));
        Assert.Contains(tablesHash, provenance, StringComparison.Ordinal);
        Assert.Contains("CC0", File.ReadAllText(first.NoticePath), StringComparison.Ordinal);

        CompiledTableFile file = CompiledTableFile.Read(File.ReadAllBytes(first.TablesPath));
        Assert.Equal(["colors", "numbers"], file.Names.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_changed_file_fails_before_anything_is_converted_or_written()
    {
        string files = Path.Combine(_work, "files");
        Directory.CreateDirectory(files);
        File.Copy(Path.Combine(Files, "numbers.txt"), Path.Combine(files, "numbers.txt"));
        File.WriteAllText(Path.Combine(files, "colors.txt"), "red\ngreen\nblue\nyellow\n");
        string output = Path.Combine(_work, "out");

        PinMismatchException e = Assert.Throws<PinMismatchException>(() => IngestRunner.Run(Pin(), files, output, new SyntheticSource()));
        Assert.Contains("colors.txt", e.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(output));
    }

    [Fact]
    public void A_missing_file_is_reported_by_path()
    {
        PinMismatchException e = Assert.Throws<PinMismatchException>(() => IngestRunner.Run(Pin(), _work, Path.Combine(_work, "out"), new SyntheticSource()));
        Assert.Contains("missing", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_command_line_entry_point_maps_failures_to_exit_codes()
    {
        string pin = Path.Combine(Fixture, "PIN");
        string output = Path.Combine(_work, "cli");
        StringWriter errors = new();
        TextWriter savedError = Console.Error;
        TextWriter savedOut = Console.Out;
        Console.SetError(errors);
        Console.SetOut(TextWriter.Null);
        try
        {
            Assert.Equal(0, IngestRunner.Main(["--pin", pin, "--files", Files, "--out", output], new SyntheticSource()));
            Assert.True(File.Exists(Path.Combine(output, "synthetic.ocd")));
            Assert.Equal(2, IngestRunner.Main(["--pin", pin, "--files", _work, "--out", output], new SyntheticSource()));
            Assert.Equal(64, IngestRunner.Main(["--pin", pin], new SyntheticSource()));
            Assert.Equal(64, IngestRunner.Main(["--bogus", "x", "--pin", pin, "--out", output], new SyntheticSource()));
            File.WriteAllText(Path.Combine(_work, "bad"), "source synthetic\n");
            Assert.Equal(3, IngestRunner.Main(["--pin", Path.Combine(_work, "bad"), "--out", output], new SyntheticSource()));
        }
        finally
        {
            Console.SetError(savedError);
            Console.SetOut(savedOut);
        }
    }

    [Theory]
    [InlineData("file ../x 0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("file /abs 0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("file a//b 0000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("file ok.txt notahash")]
    [InlineData("bogus entry")]
    [InlineData("source twice")]
    public void A_bad_pin_is_refused(string line)
    {
        string[] valid = ["source s", "licence MIT", "repo r", "ref v1", "urlbase https://example.invalid/"];
        Assert.Throws<FormatException>(() => PinFile.Parse([.. valid, line]));
    }

    [Fact]
    public void A_pin_needs_every_entry_and_at_least_one_file()
    {
        Assert.Throws<FormatException>(() => PinFile.Parse(["source s"]));
        Assert.Throws<FormatException>(() => PinFile.Parse(["source s", "licence MIT", "repo r", "ref v1", "urlbase u"]));
        string hash = new('a', 64);
        PinFile pin = PinFile.Parse(["# comment", "", "source s", "licence MIT", "repo r", "ref v1", "urlbase u", "file a/b.txt " + hash.ToUpperInvariant()]);
        Assert.Equal(hash, pin.Files[0].Sha256);
    }

    // --- the table file reader on damaged and hostile images ---

    [Fact]
    public void The_reader_rejects_damaged_and_hostile_files()
    {
        TableSetWriter w = new();
        w.Add("t", [1, 2, 3]);
        byte[] good = w.ToArray();
        Assert.Equal(3, CompiledTableFile.Read(good).TryGetTable("t", out ReadOnlyMemory<byte> t) ? t.Length : -1);

        Assert.Throws<InvalidDataException>(() => CompiledTableFile.Read(Array.Empty<byte>()));
        Assert.Throws<InvalidDataException>(() => CompiledTableFile.Read("XXXXXXXXXXXXXXXX"u8.ToArray()));

        // Every truncation of a good file is refused (never an exception of another type).
        for (int cut = 0; cut < good.Length; cut++)
        {
            byte[] truncated = good[..cut];
            Assert.Throws<InvalidDataException>(() => CompiledTableFile.Read(truncated));
        }

        byte[] version = (byte[])good.Clone();
        version[4] = 9;
        Assert.Throws<InvalidDataException>(() => CompiledTableFile.Read(version));

        byte[] huge = (byte[])good.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(huge.AsSpan(8), uint.MaxValue);
        Assert.Throws<InvalidDataException>(() => CompiledTableFile.Read(huge));

        // A table that points past the end of the file, with offset + length overflowing 32 bits.
        byte[] outside = (byte[])good.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(outside.AsSpan(14), 0xFFFFFFF0);
        BinaryPrimitives.WriteUInt32LittleEndian(outside.AsSpan(18), 0x20);
        Assert.Throws<InvalidDataException>(() => CompiledTableFile.Read(outside));
    }

    [Fact]
    public void The_writer_orders_tables_by_name_and_refuses_bad_names()
    {
        TableSetWriter a = new();
        a.Add("b", [2]);
        a.Add("a", [1]);
        TableSetWriter b = new();
        b.Add("a", [1]);
        b.Add("b", [2]);
        Assert.Equal(a.ToArray(), b.ToArray());
        Assert.Throws<ArgumentException>(() => a.Add("a", []));
        Assert.Throws<ArgumentException>(() => a.Add("", []));
        Assert.Throws<ArgumentException>(() => a.Add(new string('x', 256), []));
    }

    [Fact]
    public void The_core_assembly_carries_no_embedded_test_data()
    {
        Assembly core = typeof(Region).Assembly;
        Assert.DoesNotContain(core.GetManifestResourceNames(), n => n.Contains("synthetic", StringComparison.Ordinal));
    }
}
