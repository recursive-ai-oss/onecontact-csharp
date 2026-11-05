using System.Text;
using RecursiveAI.OneContact.Web;

namespace RecursiveAI.OneContact.Keys;

/// <summary>
/// Computes the comparison key of a <see cref="WebAddress"/>. The key is the aggressive, versioned form (see <see cref="KeyVersion"/>);
/// every rule of a version is written in <c>docs/web.md</c> and a published version's output never changes.
/// </summary>
public static class WebKey
{
    // Version 1 drops these final path segments: they name the default document of a directory, not a different page.
    private static readonly string[] DefaultDocuments = ["index.html", "index.htm", "index.php", "index.asp", "index.aspx", "default.htm", "default.html", "default.asp", "default.aspx"];

    /// <summary>Computes the key of <paramref name="value"/> under key version <paramref name="version"/>.</summary>
    /// <param name="value">The value.</param>
    /// <param name="version">The key version.</param>
    /// <returns>The key; empty for the default value.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is not a defined <see cref="KeyVersion"/>.</exception>
    public static string Compute(WebAddress value, KeyVersion version)
    {
        KeyVersions.ThrowIfUndefined(version);
        WebParts? p = value.Parts;
        if (p is null)
        {
            return string.Empty;
        }

        // Version 1, rule by rule (docs/web.md):
        var sb = new StringBuilder();

        // 1. The scheme is left out for http and https, and kept for every other scheme.
        bool web = p.Scheme is "http" or "https";
        if (!web)
        {
            sb.Append(p.Scheme).Append(':');
        }

        // A URL with an opaque path (mailto:, tel:, ...) has no host to reduce: its path, lowercased, is the key.
        if (p.OpaquePath is not null)
        {
            sb.Append(p.OpaquePath.ToLowerInvariant());
            return sb.ToString();
        }

        // 2. A domain host is reduced to its registrable domain (the public suffix plus one label), a trailing root dot dropped, so
        //    "www.", "m." and any other subdomain do not tell two addresses of one organization apart. A host that has no registrable
        //    domain (an IP address, a name that is itself a public suffix, an opaque host) is kept whole.
        string host = p.Host;
        if (p.HostKind == HostKind.Domain)
        {
            host = p.RegistrableDomain ?? (host.EndsWith('.') ? host[..^1] : host);
        }

        if (!web)
        {
            sb.Append("//");
        }

        sb.Append(host);

        // 3. A port that is not the scheme's default is kept (the canonical form already leaves a default port out).
        if (p.Port >= 0)
        {
            sb.Append(':').Append(p.Port);
        }

        // 4. The path is normalized; 5. the query's parameters are sorted. Credentials and the fragment are not part of the key.
        sb.Append(NormalizePath(p.Path));
        if (!string.IsNullOrEmpty(p.Query))
        {
            string[] pairs = p.Query.Split('&', StringSplitOptions.RemoveEmptyEntries);
            Array.Sort(pairs, StringComparer.Ordinal);
            if (pairs.Length > 0)
            {
                sb.Append('?').Append(string.Join('&', pairs));
            }
        }

        return sb.ToString();
    }

    // Rule 4: ASCII letters lowercased (hex digits of percent-escapes uppercase); escapes of unreserved characters decoded; runs of
    // "/" collapsed; a default-document segment and the trailing "/" dropped.
    private static string NormalizePath(string path)
    {
        var sb = new StringBuilder(path.Length);
        for (int i = 0; i < path.Length; i++)
        {
            char c = path[i];
            if (c == '%' && i + 2 < path.Length && PercentEncoding.IsHex(path[i + 1]) && PercentEncoding.IsHex(path[i + 2]))
            {
                int b = (PercentEncoding.HexValue(path[i + 1]) << 4) | PercentEncoding.HexValue(path[i + 2]);
                if (b is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '.' or '_' or '~')
                {
                    sb.Append(char.ToLowerInvariant((char)b));
                }
                else
                {
                    sb.Append('%').Append("0123456789ABCDEF"[b >> 4]).Append("0123456789ABCDEF"[b & 15]);
                }

                i += 2;
            }
            else if (c is >= 'A' and <= 'Z')
            {
                sb.Append((char)(c | 0x20));
            }
            else
            {
                sb.Append(c);
            }
        }

        string[] segments = sb.ToString().Split('/', StringSplitOptions.RemoveEmptyEntries);
        int count = segments.Length;
        if (count > 0 && Array.IndexOf(DefaultDocuments, segments[count - 1]) >= 0)
        {
            count--;
        }

        return count == 0 ? string.Empty : "/" + string.Join('/', segments, 0, count);
    }
}
