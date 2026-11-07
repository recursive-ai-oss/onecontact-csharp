using RecursiveAI.OneContact.Addresses.Parsing;

namespace RecursiveAI.OneContact.Tests.Addresses;

/// <summary>Finds the committed test data under <c>testdata/</c>: the repository root above the test binaries, or, when the build output
/// lives outside the checkout (as in CI), the root above this source file.</summary>
internal static class AddressTestData
{
    internal static string Root
    {
        get
        {
            string? dir = AppContext.BaseDirectory;
            while (dir is not null && !File.Exists(Path.Combine(dir, "OneContact.slnx")))
            {
                dir = Path.GetDirectoryName(dir);
            }

            dir ??= SourceRoot();
            return File.Exists(Path.Combine(dir, "OneContact.slnx"))
                ? dir
                : throw new DirectoryNotFoundException("The repository root (OneContact.slnx) was not found above the test binaries or the test sources.");
        }
    }

    private static string SourceRoot([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", ".."));

    internal static IEnumerable<string[]> Rows(string relativePath, int columns)
    {
        foreach (string line in File.ReadLines(Path.Combine(Root, relativePath)))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] cells = line.Split('\t');
            if (cells.Length != columns)
            {
                throw new InvalidDataException($"{relativePath}: expected {columns} columns, found {cells.Length} in '{line}'.");
            }

            yield return cells;
        }
    }

    /// <summary>
    /// Whether reading the text without a hint names no country: the text is refused, or its fields are returned with the country left unknown (every country that can read it
    /// finds the same fields). A country that is guessed makes this false.
    /// </summary>
    internal static bool NamesNoCountry(string text) =>
        RuleAddressParser.TryParse(text, Region.Unknown, out ParsedAddress? address, out _) != System.Buffers.OperationStatus.Done || address is null || address.Country.IsUnknown;
}
