using System.Reflection;

namespace RecursiveAI.OneContact.Data;

/// <summary>
/// Loads a compiled table file that was embedded in an assembly at build time, once, on first use. Embedded resources are
/// ordinary data to the trimmer and NativeAOT (no reflection over types), and nothing is read until a table is asked for, so
/// applications that never touch a data set never pay for it.
/// </summary>
internal sealed class EmbeddedTables
{
    private readonly Lazy<CompiledTableFile> _file;

    /// <summary>Describes an embedded file without reading it.</summary>
    /// <param name="assembly">The assembly that holds the resource.</param>
    /// <param name="resourceName">The resource's logical name, <c>OneContact.Data.&lt;source&gt;.ocd</c> by convention.</param>
    internal EmbeddedTables(Assembly assembly, string resourceName)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(resourceName);
        _file = new Lazy<CompiledTableFile>(
            () => CompiledTableFile.Read(ReadResource(assembly, resourceName)),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>Gets a value indicating whether the file has been read yet.</summary>
    internal bool IsLoaded => _file.IsValueCreated;

    /// <summary>Gets the named table, reading the file on first use.</summary>
    /// <param name="name">The table name.</param>
    /// <returns>The table bytes.</returns>
    /// <exception cref="InvalidDataException">The resource is missing, damaged, or has no such table.</exception>
    internal ReadOnlyMemory<byte> GetTable(string name)
    {
        if (!_file.Value.TryGetTable(name, out ReadOnlyMemory<byte> table))
        {
            throw new InvalidDataException($"The compiled data has no table '{name}'.");
        }

        return table;
    }

    private static byte[] ReadResource(Assembly assembly, string resourceName)
    {
        using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException($"The compiled data '{resourceName}' is not embedded in the assembly.");
        byte[] buffer = new byte[stream.Length];
        stream.ReadExactly(buffer);
        return buffer;
    }
}
