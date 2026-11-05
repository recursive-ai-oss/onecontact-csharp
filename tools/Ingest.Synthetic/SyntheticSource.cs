using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace RecursiveAI.OneContact.Ingest.Synthetic;

/// <summary>
/// A made-up source with two tiny files. <c>colors.txt</c> becomes the table "colors" (the lines joined with a line feed, UTF-8)
/// and <c>numbers.txt</c> the table "numbers" (one little-endian 32-bit integer per line).
/// </summary>
public sealed class SyntheticSource : IIngestSource
{
    /// <inheritdoc/>
    public string Name => "synthetic";

    /// <inheritdoc/>
    public string ToolVersion => "1";

    /// <inheritdoc/>
    public string NoticeText => "Synthetic test data written for this repository. Dedicated to the public domain under CC0 1.0.\n";

    /// <inheritdoc/>
    public void Convert(IngestInput input, TableSetWriter tables)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(tables);
        string[] colors = Lines(input.ReadAllBytes("colors.txt"));
        tables.Add("colors", Encoding.UTF8.GetBytes(string.Join('\n', colors)));

        string[] numbers = Lines(input.ReadAllBytes("numbers.txt"));
        byte[] packed = new byte[numbers.Length * 4];
        for (int i = 0; i < numbers.Length; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(packed.AsSpan(i * 4), int.Parse(numbers[i], NumberStyles.None, CultureInfo.InvariantCulture));
        }

        tables.Add("numbers", packed);
    }

    private static string[] Lines(byte[] bytes) =>
        Encoding.UTF8.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries);
}
