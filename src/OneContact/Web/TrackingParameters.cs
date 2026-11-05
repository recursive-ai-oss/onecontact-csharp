using System.Collections.Frozen;
using System.Text;

namespace RecursiveAI.OneContact.Web;

/// <summary>
/// The tracking parameters the canonical form of a <see cref="WebAddress"/> removes: query parameters that advertising and email
/// platforms append to a link to identify a click or a campaign and that do not choose what the page shows. The list is our own,
/// built from each platform's published documentation (the source of each name is the comment beside it, read 2026-11-05); a name
/// that no platform documents is not on it. Names are matched ignoring ASCII case, on the name as written (not percent-decoded).
/// </summary>
internal static class TrackingParameters
{
    private static readonly FrozenSet<string> Names = new[]
    {
        // Google Analytics 4, "Collect campaign data with custom URLs": https://support.google.com/analytics/answer/10917952
        "utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content", "utm_id",
        "utm_source_platform", "utm_creative_format", "utm_marketing_tactic",

        // Google Ads, "About auto-tagging": https://support.google.com/google-ads/answer/3095550 (gclid), and the Google Ads API guide
        // "Upload click conversions": https://developers.google.com/google-ads/api/docs/conversions/upload-clicks (gbraid, wbraid).
        "gclid", "gbraid", "wbraid",

        // Microsoft Advertising, "Auto-tagging of Microsoft Click ID (MSCLKID)" in the Microsoft Advertising help.
        "msclkid",

        // Meta, "ClickID and the fbp and fbc Parameters": https://developers.facebook.com/documentation/ads-commerce/conversions-api/parameters/fbp-and-fbc
        "fbclid",

        // TikTok Ads Manager, "About TikTok Click ID": https://ads.tiktok.com/resources/help/article/tiktok-click-id
        "ttclid",

        // X Ads API, "Web conversions": https://docs.x.com/x-ads-api/measurement/web-conversions
        "twclid",

        // LinkedIn Marketing API, "Enabling first-party cookies": https://learn.microsoft.com/en-us/linkedin/marketing/conversions/enabling-first-party-cookies
        "li_fat_id",

        // Mailchimp developer documentation, "E-commerce": https://mailchimp.com/developer/marketing/docs/e-commerce/
        "mc_cid", "mc_eid",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Tells whether a query parameter name is a tracking parameter.</summary>
    /// <param name="name">The name as written.</param>
    /// <returns><see langword="true"/> when the canonical form removes it.</returns>
    internal static bool IsTracking(string name) => Names.Contains(name);

    /// <summary>Removes tracking parameters from a query string, keeping every other parameter exactly as written and in order.</summary>
    /// <param name="query">The query, without the leading <c>?</c>.</param>
    /// <param name="removed">Whether anything was removed.</param>
    /// <returns>The query without tracking parameters, or <see langword="null"/> when nothing else was left.</returns>
    internal static string? Remove(string query, out bool removed)
    {
        removed = false;
        var kept = new StringBuilder();
        foreach (string pair in query.Split('&'))
        {
            int equals = pair.IndexOf('=', StringComparison.Ordinal);
            string name = equals < 0 ? pair : pair[..equals];
            if (name.Length > 0 && IsTracking(name))
            {
                removed = true;
                continue;
            }

            if (kept.Length > 0)
            {
                kept.Append('&');
            }

            kept.Append(pair);
        }

        if (!removed)
        {
            return query;
        }

        return kept.Length == 0 ? null : kept.ToString();
    }
}
