using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using RecursiveAI.OneContact.Data;

namespace RecursiveAI.OneContact.Phones.Geo.Data;

/// <summary>
/// One embedded compiled table file, read on first use, with its prefix maps opened (and checked) when first asked for.
/// Embedded resources are plain data to the trimmer and NativeAOT.
/// </summary>
internal sealed class GeoTables
{
    private readonly Lazy<CompiledTableFile> _file;
    private readonly ConcurrentDictionary<string, PrefixMap?> _maps = new(StringComparer.Ordinal);
    private readonly Lazy<Dictionary<int, string[]>> _languagesByCallingCode;

    /// <summary>Describes an embedded table file without reading it.</summary>
    /// <param name="source">The source name, which names the resource <c>OneContact.Data.&lt;source&gt;.ocd</c>.</param>
    internal GeoTables(string source)
        : this(() => ReadResource(typeof(GeoTables).Assembly, "OneContact.Data." + source + ".ocd"))
    {
    }

    /// <summary>Describes a table file supplied by the caller (tests use this for damaged files).</summary>
    /// <param name="image">Produces the file image when first needed.</param>
    internal GeoTables(Func<byte[]> image)
    {
        _file = new Lazy<CompiledTableFile>(() => CompiledTableFile.Read(image()), LazyThreadSafetyMode.ExecutionAndPublication);
        _languagesByCallingCode = new Lazy<Dictionary<int, string[]>>(IndexLanguages, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>Gets a value indicating whether the table file has been read.</summary>
    internal bool IsLoaded => _file.IsValueCreated;

    /// <summary>Gets the opened prefix maps, which shows how much has been decoded.</summary>
    internal int OpenedMaps => _maps.Count;

    /// <summary>Finds a prefix map by table name.</summary>
    /// <param name="table">The table name, such as <c>g/en/49</c>.</param>
    /// <param name="map">The map.</param>
    /// <returns><see langword="true"/> when the table exists.</returns>
    /// <exception cref="InvalidDataException">The file or the table is damaged.</exception>
    internal bool TryGetMap(string table, out PrefixMap map)
    {
        PrefixMap? cached = _maps.GetOrAdd(table, name => _file.Value.TryGetTable(name, out ReadOnlyMemory<byte> bytes) ? new PrefixMap(bytes) : null);
        if (cached is { } found)
        {
            map = found;
            return true;
        }

        map = default;
        return false;
    }

    /// <summary>Gets a table's bytes.</summary>
    /// <param name="table">The table name.</param>
    /// <param name="bytes">The bytes.</param>
    /// <returns><see langword="true"/> when the table exists.</returns>
    internal bool TryGetTable(string table, out ReadOnlyMemory<byte> bytes) => _file.Value.TryGetTable(table, out bytes);

    /// <summary>Gets the table names, in directory order.</summary>
    internal IEnumerable<string> TableNames => _file.Value.Names;

    /// <summary>Lists the languages a calling code has tables for, in ordinal order, for a table family such as <c>g</c> or <c>c</c>.</summary>
    /// <param name="callingCode">The calling code.</param>
    /// <returns>The language names as the data folders spell them (<c>zh_Hant</c>).</returns>
    internal IReadOnlyList<string> LanguagesFor(int callingCode) =>
        _languagesByCallingCode.Value.TryGetValue(callingCode, out string[]? languages) ? languages : [];

    private Dictionary<int, string[]> IndexLanguages()
    {
        Dictionary<int, List<string>> byCode = [];
        foreach (string name in _file.Value.Names)
        {
            string[] parts = name.Split('/');
            if (parts.Length == 3 && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int code))
            {
                if (!byCode.TryGetValue(code, out List<string>? list))
                {
                    byCode.Add(code, list = []);
                }

                list.Add(parts[1]);
            }
        }

        Dictionary<int, string[]> result = [];
        foreach ((int code, List<string> list) in byCode)
        {
            list.Sort(StringComparer.Ordinal);
            result.Add(code, [.. list]);
        }

        return result;
    }

    private static byte[] ReadResource(Assembly assembly, string resourceName)
    {
        using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException($"The compiled data '{resourceName}' is not embedded in this assembly.");
        using MemoryStream copy = new((int)Math.Min(stream.Length, int.MaxValue));
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
