using System.Globalization;
using System.Text;

namespace RecursiveAI.OneContact.Web;

/// <summary>A URL record of the WHATWG URL Standard (section 4.1), with serialization and the getters the URL API defines.</summary>
internal sealed class UrlRecord
{
    internal string Scheme { get; set; } = string.Empty;

    internal string Username { get; set; } = string.Empty;

    internal string Password { get; set; } = string.Empty;

    /// <summary>Gets or sets the serialized host, or <see langword="null"/> when the URL has none. The empty string is the empty host.</summary>
    internal string? Host { get; set; }

    internal HostKind HostKind { get; set; }

    /// <summary>Gets or sets the port, or -1 for null.</summary>
    internal int Port { get; set; } = -1;

    /// <summary>Gets the path segments of a URL whose path is a list.</summary>
    internal List<string> Path { get; private set; } = [];

    /// <summary>Gets or sets the path of a URL that has an opaque path (a single string), or <see langword="null"/> when the path is a list.</summary>
    internal string? OpaquePath { get; set; }

    internal string? Query { get; set; }

    internal string? Fragment { get; set; }

    internal bool IsSpecial => SpecialSchemes.IsSpecial(Scheme);

    internal bool HasOpaquePath => OpaquePath is not null;

    internal bool IncludesCredentials => Username.Length > 0 || Password.Length > 0;

    // Section 4.2: a URL cannot have a username, password or port when its host is null or empty, or its scheme is file.
    internal bool CannotHaveCredentialsOrPort => string.IsNullOrEmpty(Host) || Scheme == "file";

    internal UrlRecord Clone() => new()
    {
        Scheme = Scheme,
        Username = Username,
        Password = Password,
        Host = Host,
        HostKind = HostKind,
        Port = Port,
        Path = [.. Path],
        OpaquePath = OpaquePath,
        Query = Query,
        Fragment = Fragment,
    };

    internal void SetPath(List<string> path) => Path = path;

    // Serializing is done often and in short strings: one builder per thread is reused instead of allocating one each time.
    [ThreadStatic]
    private static StringBuilder? t_builder;

    private static StringBuilder Rent()
    {
        StringBuilder? sb = t_builder;
        if (sb is null)
        {
            return new StringBuilder(128);
        }

        t_builder = null;
        return sb.Clear();
    }

    private static string Return(StringBuilder sb)
    {
        string result = sb.ToString();
        if (sb.Capacity <= 4096)
        {
            t_builder = sb;
        }

        return result;
    }

    /// <summary>Section 4.5, the URL serializer.</summary>
    /// <param name="excludeFragment">Whether to leave the fragment out.</param>
    /// <returns>The serialized URL.</returns>
    internal string Serialize(bool excludeFragment = false)
    {
        StringBuilder sb = Rent();
        sb.Append(Scheme).Append(':');
        if (Host is not null)
        {
            sb.Append("//");
            if (IncludesCredentials)
            {
                sb.Append(Username);
                if (Password.Length > 0)
                {
                    sb.Append(':').Append(Password);
                }

                sb.Append('@');
            }

            sb.Append(Host);
            if (Port >= 0)
            {
                sb.Append(':').Append(Port.ToString(CultureInfo.InvariantCulture));
            }
        }

        // A list path that starts with an empty segment would read as an authority without this marker.
        if (Host is null && !HasOpaquePath && Path.Count > 1 && Path[0].Length == 0)
        {
            sb.Append("/.");
        }

        AppendPath(sb);
        if (Query is not null)
        {
            sb.Append('?').Append(Query);
        }

        if (!excludeFragment && Fragment is not null)
        {
            sb.Append('#').Append(Fragment);
        }

        return Return(sb);
    }

    internal string SerializePath()
    {
        if (OpaquePath is not null)
        {
            return OpaquePath;
        }

        if (Path.Count == 1)
        {
            return string.Concat("/", Path[0]);
        }

        StringBuilder sb = Rent();
        AppendPath(sb);
        return Return(sb);
    }

    // The path as the serializer writes it: the opaque path, or each segment after a slash.
    private void AppendPath(StringBuilder sb)
    {
        if (OpaquePath is not null)
        {
            sb.Append(OpaquePath);
            return;
        }

        foreach (string segment in Path)
        {
            sb.Append('/').Append(segment);
        }
    }

    // The getters of the URL API (section 6.1), used to compare against the conformance data.
    internal string ProtocolText => Scheme + ":";

    internal string HostText => Host is null ? string.Empty : Port < 0 ? Host : Host + ":" + Port.ToString(CultureInfo.InvariantCulture);

    internal string HostnameText => Host ?? string.Empty;

    internal string PortText => Port < 0 ? string.Empty : Port.ToString(CultureInfo.InvariantCulture);

    internal string SearchText => string.IsNullOrEmpty(Query) ? string.Empty : "?" + Query;

    internal string HashText => string.IsNullOrEmpty(Fragment) ? string.Empty : "#" + Fragment;

    /// <summary>Section 4.7, the serialization of the origin of the URL ("null" for an opaque origin).</summary>
    internal string OriginText
    {
        get
        {
            switch (Scheme)
            {
                case "blob":
                    if (UrlParser.TryParse(SerializePath(), null, out UrlRecord inner, out _) && inner.Scheme is "http" or "https" or "file")
                    {
                        return inner.OriginText;
                    }

                    return "null";
                case "ftp":
                case "http":
                case "https":
                case "ws":
                case "wss":
                    return Scheme + "://" + HostText;
                default:
                    return "null";
            }
        }
    }
}

/// <summary>The special schemes and their default ports (section 4.2).</summary>
internal static class SpecialSchemes
{
    internal static bool IsSpecial(string scheme) => scheme is "ftp" or "file" or "http" or "https" or "ws" or "wss";

    /// <summary>Gets the default port of a scheme, or -1 when it has none.</summary>
    /// <param name="scheme">The scheme.</param>
    /// <returns>The port or -1.</returns>
    internal static int DefaultPort(string scheme) => scheme switch
    {
        "ftp" => 21,
        "http" or "ws" => 80,
        "https" or "wss" => 443,
        _ => -1,
    };
}
