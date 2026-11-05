using System.Collections.Frozen;

namespace RecursiveAI.OneContact.Web;

/// <summary>
/// Recognizes the shape of social-profile addresses: the host and the form of the path. These are rules we wrote from how the networks
/// shape profile addresses (X handles of up to 15 letters, digits and underscores; Instagram names of up to 30 letters, digits, periods
/// and underscores; GitHub names of up to 39 letters, digits and hyphens). A match says the address has the shape of a profile, not
/// that the profile exists.
/// </summary>
internal static class SocialProfiles
{
    private static readonly FrozenSet<string> XReserved = Set(
        "home", "explore", "notifications", "messages", "search", "settings", "i", "intent", "share", "compose", "login", "logout",
        "signup", "tos", "privacy", "about", "hashtag", "download", "jobs", "account", "oauth", "who_to_follow", "lists", "topics");

    private static readonly FrozenSet<string> GitHubReserved = Set(
        "about", "pricing", "features", "login", "join", "settings", "notifications", "explore", "marketplace", "topics", "trending",
        "collections", "sponsors", "enterprise", "security", "apps", "pulls", "issues", "search", "sessions", "new", "codespaces",
        "readme", "site", "contact", "customer-stories", "events", "team", "solutions", "resources", "why-github", "copilot",
        "premium-support", "github-copilot", "login", "logout", "password_reset", "organizations", "account", "dashboard", "stars",
        "watching", "discussions", "models", "mobile", "partners", "readme", "git-guides", "docs", "support", "terms", "privacy");

    private static readonly FrozenSet<string> FacebookReserved = Set(
        "sharer", "sharer.php", "share", "share.php", "dialog", "plugins", "tr", "login", "login.php", "recover", "events", "help",
        "policies", "privacy", "legal", "watch", "marketplace", "gaming", "stories", "ads", "business", "l.php", "photo", "photo.php",
        "permalink.php", "hashtag", "reel", "video", "videos", "story.php", "home.php", "settings", "bookmarks", "friends", "messages",
        "notifications", "pages", "public", "search", "about", "careers", "terms", "cookies", "security");

    private static readonly FrozenSet<string> InstagramReserved = Set(
        "p", "reel", "reels", "explore", "accounts", "stories", "direct", "tv", "about", "legal", "developer", "web", "challenge",
        "directory", "press", "privacy", "terms", "api", "oauth", "emails", "session", "graphql", "locations", "tags", "login", "help");

    // Hosts that already use /@name for something we name separately, or where it means something other than a profile on a
    // Mastodon-style server.
    private static readonly string[] AtPathHosts = ["tiktok.com", "youtube.com", "medium.com", "substack.com", "threads.net", "threads.com"];

    /// <summary>Detects the social-profile shape of an address.</summary>
    /// <param name="scheme">The scheme.</param>
    /// <param name="host">The ASCII host.</param>
    /// <param name="path">The canonical path.</param>
    /// <returns>The matching <c>Social...</c> flag, or <see cref="WebFacts.None"/>.</returns>
    internal static WebFacts Detect(string scheme, string host, string path)
    {
        if (scheme is not ("http" or "https") || host.Length == 0)
        {
            return WebFacts.None;
        }

        string[] segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return WebFacts.None;
        }

        string first = segments[0];
        if (HostIs(host, "linkedin.com"))
        {
            return segments.Length >= 2 && first is "in" or "company" or "school" or "showcase" or "pub" ? WebFacts.SocialLinkedIn : WebFacts.None;
        }

        if (HostIs(host, "twitter.com") || HostIs(host, "x.com"))
        {
            return segments.Length == 1 && IsHandle(first, 15, allowDot: false, allowHyphen: false) && !XReserved.Contains(first) ? WebFacts.SocialX : WebFacts.None;
        }

        if (HostIs(host, "github.com"))
        {
            if (first == "orgs" && segments.Length >= 2)
            {
                return WebFacts.SocialGitHub;
            }

            return IsHandle(first, 39, allowDot: false, allowHyphen: true) && !GitHubReserved.Contains(first.ToLowerInvariant()) ? WebFacts.SocialGitHub : WebFacts.None;
        }

        if (HostIs(host, "facebook.com") || HostIs(host, "fb.com"))
        {
            bool page = first is "profile.php" or "people" or "groups" || !FacebookReserved.Contains(first.ToLowerInvariant());
            return page ? WebFacts.SocialFacebook : WebFacts.None;
        }

        if (HostIs(host, "instagram.com"))
        {
            return segments.Length == 1 && IsHandle(first, 30, allowDot: true, allowHyphen: false) && !InstagramReserved.Contains(first.ToLowerInvariant()) ? WebFacts.SocialInstagram : WebFacts.None;
        }

        if (HostIs(host, "youtube.com"))
        {
            bool channel = (first.Length > 1 && first[0] == '@') || (segments.Length >= 2 && first is "channel" or "c" or "user");
            return channel ? WebFacts.SocialYouTube : WebFacts.None;
        }

        if (HostIs(host, "tiktok.com"))
        {
            return segments.Length == 1 && first.Length > 1 && first[0] == '@' ? WebFacts.SocialTikTok : WebFacts.None;
        }

        if (segments.Length == 1 && IsAtName(first))
        {
            foreach (string excluded in AtPathHosts)
            {
                if (HostIs(host, excluded))
                {
                    return WebFacts.None;
                }
            }

            return WebFacts.SocialMastodonStyle;
        }

        return WebFacts.None;
    }

    private static bool HostIs(string host, string domain) =>
        host.Equals(domain, StringComparison.Ordinal) || host.EndsWith("." + domain, StringComparison.Ordinal);

    private static bool IsHandle(string s, int maxLength, bool allowDot, bool allowHyphen)
    {
        if (s.Length is 0 || s.Length > maxLength)
        {
            return false;
        }

        foreach (char c in s)
        {
            bool ok = c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' || (allowDot && c == '.') || (allowHyphen && c == '-');
            if (!ok)
            {
                return false;
            }
        }

        return true;
    }

    // "@name": at least one more character, starting with a letter, digit or underscore, then letters, digits, underscores, periods or hyphens.
    private static bool IsAtName(string s)
    {
        if (s.Length < 2 || s[0] != '@')
        {
            return false;
        }

        for (int i = 1; i < s.Length; i++)
        {
            char c = s[i];
            bool ok = c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' || (i > 1 && c is '.' or '-');
            if (!ok)
            {
                return false;
            }
        }

        return true;
    }

    private static FrozenSet<string> Set(params string[] values) => values.ToFrozenSet(StringComparer.Ordinal);
}
