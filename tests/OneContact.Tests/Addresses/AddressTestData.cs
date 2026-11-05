namespace RecursiveAI.OneContact.Tests.Addresses;

/// <summary>Finds the committed test data under <c>testdata/</c> by walking up from the test binaries to the repository root.</summary>
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

            return dir ?? throw new DirectoryNotFoundException("The repository root (OneContact.slnx) was not found above the test binaries.");
        }
    }

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
}
