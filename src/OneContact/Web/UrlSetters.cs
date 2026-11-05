namespace RecursiveAI.OneContact.Web;

/// <summary>
/// The setters of the WHATWG URL API (section 6.1), working on a <see cref="UrlRecord"/>. They exist so the Standard's setter
/// conformance data can run against the parser's state overrides; the public API does not expose them.
/// </summary>
internal static class UrlSetters
{
    internal static void SetProtocol(UrlRecord url, string value) => UrlParser.Override(url, value + ":", UrlState.SchemeStart);

    internal static void SetUsername(UrlRecord url, string value)
    {
        if (url.CannotHaveCredentialsOrPort)
        {
            return;
        }

        url.Username = PercentEncoding.Encode(value, PercentEncodeSet.Userinfo);
    }

    internal static void SetPassword(UrlRecord url, string value)
    {
        if (url.CannotHaveCredentialsOrPort)
        {
            return;
        }

        url.Password = PercentEncoding.Encode(value, PercentEncodeSet.Userinfo);
    }

    internal static void SetHost(UrlRecord url, string value)
    {
        if (!url.HasOpaquePath)
        {
            UrlParser.Override(url, value, UrlState.Host);
        }
    }

    internal static void SetHostname(UrlRecord url, string value)
    {
        if (!url.HasOpaquePath)
        {
            UrlParser.Override(url, value, UrlState.Hostname);
        }
    }

    internal static void SetPort(UrlRecord url, string value)
    {
        if (url.CannotHaveCredentialsOrPort)
        {
            return;
        }

        if (value.Length == 0)
        {
            url.Port = -1;
        }
        else
        {
            UrlParser.Override(url, value, UrlState.Port);
        }
    }

    internal static void SetPathname(UrlRecord url, string value)
    {
        if (url.HasOpaquePath)
        {
            return;
        }

        url.Path.Clear();
        UrlParser.Override(url, value, UrlState.PathStart);
    }

    internal static void SetSearch(UrlRecord url, string value)
    {
        if (value.Length == 0)
        {
            url.Query = null;
            return;
        }

        string input = value[0] == '?' ? value[1..] : value;
        url.Query = string.Empty;
        UrlParser.Override(url, input, UrlState.Query);
    }

    internal static void SetHash(UrlRecord url, string value)
    {
        if (value.Length == 0)
        {
            url.Fragment = null;
            return;
        }

        string input = value[0] == '#' ? value[1..] : value;
        url.Fragment = string.Empty;
        UrlParser.Override(url, input, UrlState.Fragment);
    }
}
