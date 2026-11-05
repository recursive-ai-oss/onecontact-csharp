using RecursiveAI.OneContact.Data;

namespace RecursiveAI.OneContact.Email;

/// <summary>The lists behind the email flags: role accounts, free providers, disposable domains and provider typos. Each list is documented in docs/email.md.</summary>
internal static class EmailRules
{
    private static readonly EmbeddedTables DisposableTables = new(typeof(EmailRules).Assembly, "OneContact.Data.disposable-email-domains.ocd");
    private static readonly Lazy<HashSet<string>> Disposable = new(LoadDisposable, LazyThreadSafetyMode.ExecutionAndPublication);

    // RFC 2142 section 4 (business mailboxes: info, marketing, sales, support), section 5 (network mailboxes: abuse, noc, security) and
    // section 5 (support: postmaster, hostmaster, usenet, news, webmaster, www, uucp, ftp), plus the generic role names common on contact pages.
    private static readonly HashSet<string> Roles = new(StringComparer.Ordinal)
    {
        "abuse", "accounting", "accounts", "admin", "administrator", "billing", "careers", "contact", "customerservice", "customer-service",
        "customer_service", "enquiries", "feedback", "finance", "ftp", "hello", "help", "helpdesk", "hostmaster", "hr", "info", "inquiries",
        "jobs", "legal", "mail", "mailer-daemon", "marketing", "media", "news", "newsletter", "no-reply", "no_reply", "noc", "noreply",
        "nobody", "office", "orders", "payroll", "postmaster", "press", "privacy", "recruiting", "registrar", "root", "sales", "security",
        "service", "spam", "support", "team", "usenet", "uucp", "webmaster", "www",
    };

    // Public mailbox providers anyone can sign up to without paying. Our own list; ISP mailboxes (customers only) are not on it.
    private static readonly HashSet<string> FreeProviders = new(StringComparer.Ordinal)
    {
        "gmail.com", "googlemail.com", "yahoo.com", "yahoo.co.uk", "yahoo.fr", "yahoo.de", "yahoo.es", "yahoo.it", "yahoo.ca", "yahoo.com.br",
        "yahoo.co.jp", "yahoo.co.in", "yahoo.com.au", "ymail.com", "rocketmail.com", "outlook.com", "outlook.fr", "outlook.de", "hotmail.com",
        "hotmail.co.uk", "hotmail.fr", "hotmail.de", "hotmail.it", "hotmail.es", "live.com", "live.co.uk", "live.fr", "live.nl", "msn.com",
        "aol.com", "icloud.com", "me.com", "mac.com", "proton.me", "protonmail.com", "pm.me", "gmx.com", "gmx.de", "gmx.net", "gmx.at", "web.de",
        "mail.com", "email.com", "zoho.com", "zohomail.com", "yandex.com", "yandex.ru", "mail.ru", "inbox.com", "fastmail.com", "fastmail.fm",
        "tutanota.com", "tutanota.de", "tuta.com", "tuta.io", "hey.com", "qq.com", "163.com", "126.com", "sina.com", "naver.com", "daum.net",
        "hanmail.net", "libero.it", "virgilio.it", "laposte.net", "free.fr", "orange.fr", "wanadoo.fr", "t-online.de", "freenet.de", "seznam.cz",
        "wp.pl", "o2.pl", "interia.pl", "onet.pl", "rediffmail.com", "bol.com.br", "uol.com.br", "terra.com.br", "rambler.ru", "ukr.net",
    };

    // Mistyped provider names and top-level domains, each mapped to the domain the user most likely meant. Suggestions only.
    private static readonly Dictionary<string, string> Typos = BuildTypos();

    private static Dictionary<string, string> BuildTypos()
    {
        Dictionary<string, string> map = new(StringComparer.Ordinal);
        string[] providers = ["gmail", "hotmail", "yahoo", "outlook", "icloud", "live", "aol", "msn", "protonmail"];
        string[] tldTypos = ["con", "cm", "om", "vom", "xom", "cmo", "ocm", "cim", "c0m", "comm", "coom"];
        foreach (string provider in providers)
        {
            foreach (string tld in tldTypos)
            {
                map[provider + "." + tld] = provider + ".com";
            }
        }

        string[] names =
        [
            "gmial.com=gmail.com", "gmai.com=gmail.com", "gmal.com=gmail.com", "gamil.com=gmail.com", "gmaill.com=gmail.com", "gmil.com=gmail.com",
            "gnail.com=gmail.com", "gmeil.com=gmail.com", "gmail.comm=gmail.com", "gogglemail.com=googlemail.com", "hotmial.com=hotmail.com",
            "hotmal.com=hotmail.com", "hotmai.com=hotmail.com", "hotmil.com=hotmail.com", "hotmaill.com=hotmail.com", "hotnail.com=hotmail.com",
            "yaho.com=yahoo.com", "yahooo.com=yahoo.com", "yhoo.com=yahoo.com", "yahho.com=yahoo.com", "yahou.com=yahoo.com", "outlok.com=outlook.com",
            "outloo.com=outlook.com", "otlook.com=outlook.com", "outlool.com=outlook.com", "iclould.com=icloud.com", "icoud.com=icloud.com",
            "iclod.com=icloud.com", "icloude.com=icloud.com", "protonmial.com=protonmail.com",
        ];
        foreach (string pair in names)
        {
            int eq = pair.IndexOf('=', StringComparison.Ordinal);
            map[pair[..eq]] = pair[(eq + 1)..];
        }

        return map;
    }

    private static HashSet<string> LoadDisposable()
    {
        HashSet<string> set = new(StringComparer.Ordinal);
        string all = System.Text.Encoding.UTF8.GetString(DisposableTables.GetTable("domains").Span);
        foreach (string line in all.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            set.Add(line);
        }

        return set;
    }

    /// <summary>Tells whether <paramref name="domain"/> is on the disposable list, or is a subdomain of a listed domain.</summary>
    internal static bool IsDisposable(string domain)
    {
        if (domain.Length == 0 || domain[0] == '[')
        {
            return false;
        }

        HashSet<string>.AlternateLookup<ReadOnlySpan<char>> lookup = Disposable.Value.GetAlternateLookup<ReadOnlySpan<char>>();
        ReadOnlySpan<char> rest = domain;
        while (true)
        {
            if (lookup.Contains(rest))
            {
                return true;
            }

            int dot = rest.IndexOf('.');
            if (dot < 0 || rest[(dot + 1)..].IndexOf('.') < 0)
            {
                return false; // the top-level domain alone is never listed
            }

            rest = rest[(dot + 1)..];
        }
    }

    /// <summary>Tells whether <paramref name="domain"/> is a free mailbox provider.</summary>
    internal static bool IsFreeProvider(string domain) => FreeProviders.Contains(domain);

    /// <summary>Tells whether an unquoted local part names a role: the part before a <c>+</c>, case-folded, is on the role list.</summary>
    internal static bool IsRole(string localPart)
    {
        if (localPart.Length == 0 || localPart[0] == '"')
        {
            return false;
        }

        int plus = localPart.IndexOf('+', StringComparison.Ordinal);
        string name = (plus > 0 ? localPart[..plus] : localPart).ToLowerInvariant();
        return Roles.Contains(name);
    }

    /// <summary>Gets the corrected address for a mistyped provider domain, or <see langword="null"/>.</summary>
    internal static string? SuggestTypoFix(EmailAddress address)
    {
        string domain = address.Domain;
        return Typos.TryGetValue(domain, out string? fixedDomain) ? string.Concat(address.LocalPart, "@", fixedDomain) : null;
    }

    internal static bool IsTypoDomain(string domain) => Typos.ContainsKey(domain);
}
