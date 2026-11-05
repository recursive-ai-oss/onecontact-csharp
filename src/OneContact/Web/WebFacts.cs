namespace RecursiveAI.OneContact;

/// <summary>Facts found about a <see cref="WebAddress"/>. Flags report; they never change the value.</summary>
[Flags]
public enum WebFacts : uint
{
    /// <summary>No flags.</summary>
    None = 0,

    /// <summary>The input had no scheme, and <c>https</c> was assumed.</summary>
    SchemeAssumed = 1,

    /// <summary>The host is an IPv4 or IPv6 address.</summary>
    IpHost = 1 << 1,

    /// <summary>The host is an internationalized domain name: at least one label is in its <c>xn--</c> form.</summary>
    InternationalizedDomain = 1 << 2,

    /// <summary>The scheme is not <c>http</c> or <c>https</c> (for example <c>mailto</c>, <c>ftp</c> or <c>file</c>).</summary>
    NonWebScheme = 1 << 3,

    /// <summary>The input carried a user name or password. The canonical form keeps them.</summary>
    HasCredentials = 1 << 4,

    /// <summary>The input carried tracking parameters (<c>utm_source</c>, <c>gclid</c>, ...) and the canonical form leaves them out.</summary>
    TrackingParametersRemoved = 1 << 5,

    /// <summary>The input carried a fragment (<c>#section</c>) and the canonical form leaves it out.</summary>
    FragmentRemoved = 1 << 6,

    /// <summary>The address looks like a LinkedIn profile or page (<c>/in/</c>, <c>/company/</c>, <c>/school/</c>, <c>/showcase/</c>, <c>/pub/</c>).</summary>
    SocialLinkedIn = 1 << 8,

    /// <summary>The address looks like an X (Twitter) profile: one handle of up to 15 letters, digits and underscores that is not a site page.</summary>
    SocialX = 1 << 9,

    /// <summary>The address looks like a GitHub user, organization or repository page.</summary>
    SocialGitHub = 1 << 10,

    /// <summary>The address looks like a Facebook profile or page.</summary>
    SocialFacebook = 1 << 11,

    /// <summary>The address looks like an Instagram profile.</summary>
    SocialInstagram = 1 << 12,

    /// <summary>The address looks like a YouTube channel (<c>/@handle</c>, <c>/channel/</c>, <c>/c/</c>, <c>/user/</c>).</summary>
    SocialYouTube = 1 << 13,

    /// <summary>The address looks like a TikTok profile (<c>/@handle</c>).</summary>
    SocialTikTok = 1 << 14,

    /// <summary>The path is a single <c>@name</c> segment on a host that is none of the networks above: the shape Mastodon-style servers use for profiles.</summary>
    SocialMastodonStyle = 1 << 15,

    /// <summary>Any of the social-profile flags.</summary>
    SocialProfile = SocialLinkedIn | SocialX | SocialGitHub | SocialFacebook | SocialInstagram | SocialYouTube | SocialTikTok | SocialMastodonStyle,
}
