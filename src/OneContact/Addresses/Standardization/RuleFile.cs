using System.Reflection;

namespace RecursiveAI.OneContact.Addresses.Standardization;

/// <summary>
/// Reads one of the per-country rule tables embedded in the assembly: plain UTF-8 text, one record per line, fields separated by a tab,
/// lines starting with <c>#</c> are comments. The tables are data, so adding a country means adding files, not code.
/// </summary>
internal static class RuleFile
{
    /// <summary>Reads the rows of an embedded table.</summary>
    /// <param name="fileName">The file name, such as <c>us-states.tsv</c>.</param>
    /// <returns>The rows, each an array of fields.</returns>
    /// <exception cref="InvalidDataException">The table is not embedded.</exception>
    internal static List<string[]> Read(string fileName)
    {
        using Stream stream = typeof(RuleFile).Assembly.GetManifestResourceStream("OneContact.Addresses.Data." + fileName)
            ?? throw new InvalidDataException($"The address rule table '{fileName}' is not embedded in the assembly.");
        using StreamReader reader = new(stream, System.Text.Encoding.UTF8);
        List<string[]> rows = [];
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            rows.Add(line.Split('\t'));
        }

        return rows;
    }
}
