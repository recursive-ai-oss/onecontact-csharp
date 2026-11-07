# Web addresses

`WebAddress` parses a website address with the WHATWG URL Standard and reports a canonical form, a comparison key, facts and offsets. Everything here is written from the URL Standard (living standard, last updated 19 October 2026), UTS #46, RFC 3492 and the Public Suffix List's published algorithm. Nothing depends on culture or the network.

## Parsing

- The URL parser is the Standard's basic URL parser, state by state (`Web/UrlParser.cs`), with its host parser (domains through UTS #46, IPv4 and IPv6, opaque hosts), percent-encode sets and serializer. The Standard's setters are implemented as far as its conformance data needs them, internally.
- Conformance is measured on the web-platform-tests data pinned in `upstream/wpt-url/PIN`: `urltestdata.json` (896 entries), `setters_tests.json` (278), `toascii.json` (87) and `IdnaTestV2.json` (2676). All pass. `tests/OneContact.Tests/Web/WptUrlTests.cs` runs them; the data and its BSD-3-Clause licence are under `tests/OneContact.Tests/testdata/wpt`.
- Input text is trimmed of white space. A missing scheme is assumed to be `https` and the result carries `WebFacts.SchemeAssumed`. A scheme counts as written when the text starts with `scheme:` and the colon is followed by `//`, or the scheme is one that is written without slashes (`mailto`, `tel`, `sms`, `urn`, `geo`, `callto`, `skype`, `magnet`, `news`, `data`, `javascript`, `about`, `blob`) or a special scheme. So `localhost:3000/x` and `example.com:8080` are hosts with a port, not schemes.
- A failed parse reports one of `web.host-missing`, `web.invalid-host`, `web.invalid-port`, `web.invalid-url`.
- Offsets: `ParseDetail.Fields` holds `scheme` (only when written), `host`, `port`, `path`, `query` and `fragment` spans, in UTF-16 units of the input as given (tabs and newlines the Standard removes are skipped, so spans cover them).
- Hardening limits that are not in the Standard: a host that is not ASCII and is longer than 4096 UTF-16 units fails (domain names are at most 253 octets; the limit keeps quadratic Punycode and normalization steps from being abused). ASCII hosts are never run through UTS #46, as the Standard says.

## Canonical form

The serialization of the URL with, in this order:

1. host lowercased and processed by UTS #46 (non-transitional, `CheckBidi` and `CheckJoiners` on, `UseSTD3ASCIIRules` off), so internationalized hosts appear in their `xn--` form; IPv4 numbers normalized to dotted decimal; IPv6 compressed;
2. default port left out (the Standard's rule);
3. tracking parameters removed from the query (below); a query that becomes empty disappears with its `?`; a query that was empty and had nothing removed stays;
4. the fragment removed.

Credentials, path case and every other query parameter stay as written. Removing a fragment or tracking parameters is reported in `Flags`, never silent.

### Tracking parameters

Names are matched ignoring ASCII case, on the name as written (not percent-decoded). Each name is on the list because a platform documents it; read 2026-11-05.

| Names | Platform and page |
|---|---|
| `utm_source`, `utm_medium`, `utm_campaign`, `utm_term`, `utm_content`, `utm_id`, `utm_source_platform`, `utm_creative_format`, `utm_marketing_tactic` | Google Analytics 4, "Collect campaign data with custom URLs", support.google.com/analytics/answer/10917952 |
| `gclid` | Google Ads, "About auto-tagging", support.google.com/google-ads/answer/3095550 |
| `gbraid`, `wbraid` | Google Ads API, "Upload click conversions", developers.google.com/google-ads/api/docs/conversions/upload-clicks |
| `msclkid` | Microsoft Advertising help, "Auto-tagging of Microsoft Click ID (MSCLKID)" |
| `fbclid` | Meta, "ClickID and the fbp and fbc Parameters", developers.facebook.com/documentation/ads-commerce/conversions-api/parameters/fbp-and-fbc |
| `ttclid` | TikTok Ads Manager, "About TikTok Click ID", ads.tiktok.com/resources/help/article/tiktok-click-id |
| `twclid` | X Ads API, "Web conversions", docs.x.com/x-ads-api/measurement/web-conversions |
| `li_fat_id` | LinkedIn Marketing API, "Enabling first-party cookies", learn.microsoft.com/en-us/linkedin/marketing/conversions/enabling-first-party-cookies |
| `mc_cid`, `mc_eid` | Mailchimp developer documentation, "E-commerce", mailchimp.com/developer/marketing/docs/e-commerce/ |

## Key, version 1

`KeyVersion.V1`: `WebKey.Compute` over the canonical parts. A published version's output never changes; the golden fixture is in `WebAddressTests.KeyV1GoldenFixture`.

1. The scheme is left out for `http` and `https`; every other scheme is kept as `scheme:` (and `//` when the address has a host).
2. A domain host becomes its registrable domain (the public suffix plus one label, from the Public Suffix List with its private section), so `www.`, `m.` and any other subdomain do not tell two addresses of one organization apart. A root dot is dropped. A host with no registrable domain (an IP address, an opaque host, a name that is itself a public suffix) is kept whole.
3. A port that is not the scheme's default is kept as `:port`.
4. The path is normalized: ASCII letters lowercased; `%XX` escapes of unreserved characters (letters, digits, `-` `.` `_` `~`) decoded and the hex digits of the other escapes uppercased; runs of `/` collapsed; a final segment that is a default document (`index.html`, `index.htm`, `index.php`, `index.asp`, `index.aspx`, `default.htm`, `default.html`, `default.asp`, `default.aspx`) dropped; the trailing `/` dropped (so `/` alone is empty).
5. The query's parameters (already without tracking parameters) are sorted ordinally and joined with `&`; empty parameters are dropped.
6. Credentials and the fragment are not part of the key.
7. An address with an opaque path (`mailto:`, `tel:`) has no host to reduce: the key is the scheme, `:`, and the opaque path lowercased.

## Facts

`WebFacts` reports what was found; it never changes the value: `SchemeAssumed`, `IpHost`, `InternationalizedDomain` (some label is `xn--`), `NonWebScheme` (not http or https; `IsWebsite` is false for those values: parsing accepts any WHATWG URL, including `javascript:` and `data:`, so check `IsWebsite` before using a value as a link), `HasCredentials`, `TrackingParametersRemoved`, `FragmentRemoved`, and the social-profile shapes.

### Social-profile shapes

Rules we wrote from how each network shapes profile addresses (the length limits are the networks' usual handle limits, checked against nothing but the examples in our tests); a match says the address has the shape of a profile, not that it exists. Hosts match the domain and its subdomains, for `http` and `https` only.

- LinkedIn (`linkedin.com`): `/in/<name>`, `/company/<name>`, `/school/<name>`, `/showcase/<name>`, `/pub/<name>`.
- X (`x.com`, `twitter.com`): a single segment of up to 15 letters, digits and underscores that is not a site page (`home`, `explore`, `search`, `settings`, `i`, `intent`, ...).
- GitHub (`github.com`): a first segment of up to 39 letters, digits and hyphens that is not a site page, or `/orgs/<name>/...`.
- Facebook (`facebook.com`, `fb.com`): a first segment that is not a site page (`sharer`, `login`, `events`, `watch`, ...), or `profile.php`, `people`, `groups`.
- Instagram (`instagram.com`): a single segment of up to 30 letters, digits, periods and underscores that is not a site page (`p`, `reel`, `explore`, ...).
- YouTube (`youtube.com`): `/@handle`, `/channel/<id>`, `/c/<name>`, `/user/<name>`.
- TikTok (`tiktok.com`): `/@handle` alone.
- Mastodon-style: a single `@name` segment on any other host, except hosts where `/@name` means something else (`medium.com`, `substack.com`, `threads.net`, `threads.com`).

## Public Suffix List

See `docs/psl.md`. `ContactKindDetector` treats text as a web address when it has `://`, starts with `www.`, or is a bare domain (optionally with port, path, query, fragment) whose last label is a top-level domain with a rule in the list's ICANN section, so `notes.txt`, `1.5` and `John.Smith` are not taken.
