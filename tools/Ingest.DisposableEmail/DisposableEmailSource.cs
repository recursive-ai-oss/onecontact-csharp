using System.Text;

namespace RecursiveAI.OneContact.Ingest.DisposableEmail;

/// <summary>
/// Converts <c>disposable_email_blocklist.conf</c> (one domain per line) into the table "domains": the lowercase domains, sorted
/// ordinally and without duplicates, as UTF-8 joined with a line feed.
/// </summary>
public sealed class DisposableEmailSource : IIngestSource
{
    private const string ListFile = "disposable_email_blocklist.conf";

    /// <inheritdoc/>
    public string Name => "disposable-email-domains";

    /// <inheritdoc/>
    public string ToolVersion => "1";

    /// <inheritdoc/>
    public string NoticeText =>
        "The list of disposable email domains comes from the disposable-email-domains project\n" +
        "(https://github.com/disposable-email-domains/disposable-email-domains), which dedicates it to the public domain under\n" +
        "CC0 1.0 Universal. Credit: the disposable-email-domains contributors.\n";

    /// <inheritdoc/>
    public void Convert(IngestInput input, TableSetWriter tables)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(tables);
        SortedSet<string> domains = new(StringComparer.Ordinal);
        foreach (string raw in Encoding.UTF8.GetString(input.ReadAllBytes(ListFile)).Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            domains.Add(line.ToLowerInvariant());
        }

        tables.Add("domains", Encoding.UTF8.GetBytes(string.Join('\n', domains)));
    }
}
