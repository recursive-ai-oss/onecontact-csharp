using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RecursiveAI.OneContact.Ingest;

/// <summary>What a run produced.</summary>
/// <param name="TablesPath">The compiled table file.</param>
/// <param name="ProvenancePath">The provenance record.</param>
/// <param name="NoticePath">The notice text.</param>
public sealed record IngestOutput(string TablesPath, string ProvenancePath, string NoticePath);

/// <summary>Thrown when a fetched file is missing or does not match its pinned hash.</summary>
public sealed class PinMismatchException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">The message.</param>
    public PinMismatchException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Runs one source: verify every pinned file's SHA-256 (failing before any conversion on a mismatch), convert, and write
/// <c>&lt;source&gt;.ocd</c>, <c>&lt;source&gt;.provenance.json</c> and <c>&lt;source&gt;.notice.txt</c>. Per-source tools are a
/// <c>Main</c> that calls <see cref="Main"/> with their <see cref="IIngestSource"/>.
/// </summary>
public static class IngestRunner
{
    /// <summary>Verifies, converts and writes.</summary>
    /// <param name="pin">The pin.</param>
    /// <param name="filesRoot">The directory holding the fetched files at their pinned relative paths.</param>
    /// <param name="outputDirectory">Where to write the three outputs; created if needed.</param>
    /// <param name="source">The converter.</param>
    /// <returns>The paths written.</returns>
    /// <exception cref="PinMismatchException">A file is missing or its hash differs from the pin.</exception>
    public static IngestOutput Run(PinFile pin, string filesRoot, string outputDirectory, IIngestSource source)
    {
        ArgumentNullException.ThrowIfNull(pin);
        ArgumentNullException.ThrowIfNull(source);
        if (!string.Equals(pin.Source, source.Name, StringComparison.Ordinal))
        {
            throw new PinMismatchException($"The pin is for source '{pin.Source}', not '{source.Name}'.");
        }

        Dictionary<string, byte[]> files = new(StringComparer.Ordinal);
        foreach (PinnedFile pinned in pin.Files)
        {
            string full = Path.Combine(filesRoot, pinned.Path);
            if (!File.Exists(full))
            {
                throw new PinMismatchException($"Pinned file '{pinned.Path}' is missing from '{filesRoot}'. Fetch it with tools/fetch.");
            }

            byte[] bytes = File.ReadAllBytes(full);
            string actual = Hex(SHA256.HashData(bytes));
            if (!string.Equals(actual, pinned.Sha256, StringComparison.Ordinal))
            {
                throw new PinMismatchException($"Pinned file '{pinned.Path}' has SHA-256 {actual}, but the pin says {pinned.Sha256}.");
            }

            files.Add(pinned.Path, bytes);
        }

        TableSetWriter tables = new();
        source.Convert(new IngestInput(pin, files), tables);
        byte[] image = tables.ToArray();

        Directory.CreateDirectory(outputDirectory);
        string tablesPath = Path.Combine(outputDirectory, pin.Source + ".ocd");
        string provenancePath = Path.Combine(outputDirectory, pin.Source + ".provenance.json");
        string noticePath = Path.Combine(outputDirectory, pin.Source + ".notice.txt");
        WriteIfChanged(tablesPath, image);
        WriteIfChanged(provenancePath, Provenance(pin, files, source, image));
        WriteIfChanged(noticePath, new UTF8Encoding(false).GetBytes(source.NoticeText.ReplaceLineEndings("\n")));
        return new IngestOutput(tablesPath, provenancePath, noticePath);
    }

    /// <summary>The command-line entry point of a per-source tool.</summary>
    /// <param name="args"><c>--pin &lt;file&gt; --files &lt;dir&gt; --out &lt;dir&gt;</c>. <c>--files</c> defaults to <c>$ONECONTACT_UPSTREAM/&lt;source&gt;</c>.</param>
    /// <param name="source">The converter.</param>
    /// <returns>0 on success; 2 when a pinned file is missing or its hash differs; 3 for a bad pin; 64 for bad arguments.</returns>
    public static int Main(string[] args, IIngestSource source)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(source);
        string? pinPath = null;
        string? filesRoot = null;
        string? outDir = null;
        for (int i = 0; i + 1 < args.Length; i += 2)
        {
            switch (args[i])
            {
                case "--pin":
                    pinPath = args[i + 1];
                    break;
                case "--files":
                    filesRoot = args[i + 1];
                    break;
                case "--out":
                    outDir = args[i + 1];
                    break;
                default:
                    Console.Error.WriteLine($"unknown argument '{args[i]}'");
                    return 64;
            }
        }

        if (pinPath is null || outDir is null || args.Length % 2 != 0)
        {
            Console.Error.WriteLine($"usage: {source.Name} --pin <pin file> [--files <dir>] --out <dir>");
            return 64;
        }

        try
        {
            PinFile pin = PinFile.Load(pinPath);
            filesRoot ??= Path.Combine(UpstreamRoot(), pin.Source);
            IngestOutput output = Run(pin, filesRoot, outDir, source);
            Console.WriteLine(output.TablesPath);
            return 0;
        }
        catch (PinMismatchException e)
        {
            Console.Error.WriteLine(e.Message);
            return 2;
        }
        catch (FormatException e)
        {
            Console.Error.WriteLine(e.Message);
            return 3;
        }
    }

    /// <summary>Gets the directory fetched files live in: <c>$ONECONTACT_UPSTREAM</c>, or <c>.upstream</c> in the current directory.</summary>
    /// <returns>The directory path.</returns>
    public static string UpstreamRoot()
    {
        string? env = Environment.GetEnvironmentVariable("ONECONTACT_UPSTREAM");
        return string.IsNullOrEmpty(env) ? Path.GetFullPath(".upstream") : env;
    }

    private static byte[] Provenance(PinFile pin, Dictionary<string, byte[]> files, IIngestSource source, byte[] image)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter w = new(stream, new JsonWriterOptions { Indented = true, NewLine = "\n" }))
        {
            w.WriteStartObject();
            w.WriteString("source", pin.Source);
            w.WriteString("licence", pin.Licence);
            w.WriteString("repo", pin.Repo);
            w.WriteString("ref", pin.Ref);
            w.WriteString("toolVersion", source.ToolVersion);
            w.WriteStartArray("files");
            foreach (PinnedFile f in pin.Files)
            {
                w.WriteStartObject();
                w.WriteString("path", f.Path);
                w.WriteString("sha256", f.Sha256);
                w.WriteNumber("bytes", files[f.Path].Length);
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteStartObject("output");
            w.WriteString("file", pin.Source + ".ocd");
            w.WriteString("sha256", Hex(SHA256.HashData(image)));
            w.WriteNumber("bytes", image.Length);
            w.WriteEndObject();
            w.WriteEndObject();
        }

        return [.. stream.ToArray(), (byte)'\n'];
    }

    // Leaves an identical file alone, so its timestamp does not trigger needless rebuilds downstream.
    private static void WriteIfChanged(string path, byte[] content)
    {
        if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(content))
        {
            return;
        }

        File.WriteAllBytes(path, content);
    }

    private static string Hex(byte[] hash) => Convert.ToHexStringLower(hash);
}
