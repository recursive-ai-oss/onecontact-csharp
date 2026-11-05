using System.Globalization;

namespace RecursiveAI.OneContact.Ingest;

/// <summary>One upstream file named by a pin: its path under the upstream root and the SHA-256 it must have.</summary>
/// <param name="Path">The path, relative, with forward slashes.</param>
/// <param name="Sha256">Lowercase hexadecimal SHA-256.</param>
public sealed record PinnedFile(string Path, string Sha256);

/// <summary>
/// A pin: which upstream a data set comes from, at which tag or commit, and the exact files with their hashes. Plain text, one
/// entry per line, so shell scripts can read it too:
/// <code>
/// # comment
/// source   phonenumber-metadata
/// licence  Apache-2.0
/// repo     https://github.com/owner/repo
/// ref      v1.2.3
/// urlbase  https://raw.githubusercontent.com/owner/repo/v1.2.3/
/// file     path/in/repo.xml  0123...abcd
/// </code>
/// <c>ref</c> is a tag or a full commit; <c>urlbase</c> is where each file is fetched from (the file's path is appended).
/// </summary>
public sealed class PinFile
{
    private PinFile(string source, string licence, string repo, string reference, string urlBase, IReadOnlyList<PinnedFile> files)
    {
        Source = source;
        Licence = licence;
        Repo = repo;
        Ref = reference;
        UrlBase = urlBase;
        Files = files;
    }

    /// <summary>Gets the short source name; it names the output files.</summary>
    public string Source { get; }

    /// <summary>Gets the SPDX licence identifier of the upstream data.</summary>
    public string Licence { get; }

    /// <summary>Gets the upstream repository or home page.</summary>
    public string Repo { get; }

    /// <summary>Gets the pinned tag or commit.</summary>
    public string Ref { get; }

    /// <summary>Gets the URL prefix the files are fetched from.</summary>
    public string UrlBase { get; }

    /// <summary>Gets the pinned files, in pin order.</summary>
    public IReadOnlyList<PinnedFile> Files { get; }

    /// <summary>Reads a pin file.</summary>
    /// <param name="path">The file to read.</param>
    /// <returns>The pin.</returns>
    /// <exception cref="FormatException">The pin is incomplete or malformed.</exception>
    public static PinFile Load(string path) => Parse(File.ReadAllLines(path));

    /// <summary>Parses the lines of a pin file.</summary>
    /// <param name="lines">The lines.</param>
    /// <returns>The pin.</returns>
    /// <exception cref="FormatException">The pin is incomplete or malformed.</exception>
    public static PinFile Parse(IEnumerable<string> lines)
    {
        Dictionary<string, string> single = new(StringComparer.Ordinal);
        List<PinnedFile> files = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        int lineNumber = 0;
        foreach (string raw in lines)
        {
            lineNumber++;
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            string key = parts[0];
            if (key == "file")
            {
                if (parts.Length != 3)
                {
                    throw new FormatException(string.Create(CultureInfo.InvariantCulture, $"Pin line {lineNumber}: expected 'file <path> <sha256>'."));
                }

                string filePath = parts[1];
                if (!IsSafeRelativePath(filePath))
                {
                    throw new FormatException(string.Create(CultureInfo.InvariantCulture, $"Pin line {lineNumber}: '{filePath}' is not a plain relative path."));
                }

                if (!IsSha256(parts[2]))
                {
                    throw new FormatException(string.Create(CultureInfo.InvariantCulture, $"Pin line {lineNumber}: '{parts[2]}' is not a SHA-256 in hexadecimal."));
                }

                if (!seen.Add(filePath))
                {
                    throw new FormatException(string.Create(CultureInfo.InvariantCulture, $"Pin line {lineNumber}: '{filePath}' is listed twice."));
                }

                files.Add(new PinnedFile(filePath, parts[2].ToLowerInvariant()));
            }
            else if (key is "source" or "licence" or "repo" or "ref" or "urlbase")
            {
                if (parts.Length != 2 || !single.TryAdd(key, parts[1]))
                {
                    throw new FormatException(string.Create(CultureInfo.InvariantCulture, $"Pin line {lineNumber}: '{key}' needs exactly one value and may appear once."));
                }
            }
            else
            {
                throw new FormatException(string.Create(CultureInfo.InvariantCulture, $"Pin line {lineNumber}: unknown entry '{key}'."));
            }
        }

        foreach (string required in new[] { "source", "licence", "repo", "ref", "urlbase" })
        {
            if (!single.ContainsKey(required))
            {
                throw new FormatException($"The pin has no '{required}' entry.");
            }
        }

        if (files.Count == 0)
        {
            throw new FormatException("The pin lists no files.");
        }

        return new PinFile(single["source"], single["licence"], single["repo"], single["ref"], single["urlbase"], files);
    }

    // A pinned path never climbs out of the upstream root.
    private static bool IsSafeRelativePath(string path)
    {
        if (path.StartsWith('/') || path.Contains('\\') || path.Contains(':'))
        {
            return false;
        }

        foreach (string segment in path.Split('/'))
        {
            if (segment.Length == 0 || segment is "." or "..")
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSha256(string text) =>
        text.Length == 64 && text.All(char.IsAsciiHexDigit);
}
