namespace RecursiveAI.OneContact.Ingest;

/// <summary>The verified upstream files handed to a source's converter.</summary>
public sealed class IngestInput
{
    private readonly Dictionary<string, byte[]> _files;

    internal IngestInput(PinFile pin, Dictionary<string, byte[]> files)
    {
        Pin = pin;
        _files = files;
    }

    /// <summary>Gets the pin the files were verified against.</summary>
    public PinFile Pin { get; }

    /// <summary>Gets the bytes of a pinned file, exactly as fetched.</summary>
    /// <param name="path">The path as written in the pin.</param>
    /// <returns>The bytes.</returns>
    /// <exception cref="KeyNotFoundException">The path is not in the pin.</exception>
    public byte[] ReadAllBytes(string path) => _files[path];
}

/// <summary>
/// A per-source converter: turns the pinned upstream files into compiled tables. Conversions must be deterministic (no clocks,
/// no random numbers, no dictionary-order dependence): the same pinned inputs give byte-identical tables.
/// </summary>
public interface IIngestSource
{
    /// <summary>Gets the source name; it must equal the pin's <c>source</c> entry.</summary>
    string Name { get; }

    /// <summary>Gets the version of the conversion (bump it when the output format or rules change); it goes in the provenance record.</summary>
    string ToolVersion { get; }

    /// <summary>Gets the notice text to pack with the data: licence name, copyright lines and any upstream NOTICE.</summary>
    string NoticeText { get; }

    /// <summary>Converts the verified files.</summary>
    /// <param name="input">The files.</param>
    /// <param name="tables">Where to add the compiled tables.</param>
    void Convert(IngestInput input, TableSetWriter tables);
}
