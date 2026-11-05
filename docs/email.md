# Email addresses

`EmailAddress` reads one mailbox: an `addr-spec` (`jane@example.com`) or a `name-addr` (`Jane Doe <jane@example.com>`), and gives a canonical form, a versioned key, flags and offsets into the input. `EmailParser` is the low-level form (span in, `OperationStatus` and `ParseDetail` out, plus a batch form).

## What is read

| Input | Rule |
|---|---|
| addr-spec, name-addr, angle-addr | RFC 5322 section 3.4 |
| comments and folding white space | section 3.2.2; comments are dropped and reported by the `HasComment` flag |
| quoted-string local parts, quoted-pairs | section 3.2.4; kept as written in the canonical form |
| display names | section 3.2.5 phrase; quoted strings unquoted; RFC 2047 encoded words that are a whole atom decoded (UTF-8, US-ASCII, ISO-8859-1, ISO-8859-15, windows-1252; others stay as written); adjacent encoded words join without the space between (RFC 2047 section 6.2) |
| obsolete forms | section 4 and 4.4: read and flagged `ObsoleteSyntax`, never refused. They are white space or comments around the dots of a local part or domain, a quoted word mixed with unquoted words in a local part, a route (`<@a,@b:user@example.com>`, dropped), an unquoted `.` in a display name, obsolete control characters in quoted strings, comments and domain literals, several folds in a row |
| UTF-8 | RFC 6532 section 3.2: every non-ASCII character is atext, qtext, ctext and dtext. An unpaired surrogate is refused (`email.invalid-character`) |
| domains | RFC 5321 section 4.1.2 labels (`Let-dig [Ldh-str]`) after UTS 46 processing; domain literals of section 4.1.3 |

Groups (`Name: a@x, b@y;`) and address lists are not one mailbox: they fail with `email.invalid-display-name` or `email.trailing-text`. Surrounding white space (line breaks included) is ignored, as for every value type.

## Canonical form

`<local part as written>@<domain>`. The local part is exactly what was written, quotes and quoted-pairs included (`"a b"@example.com`), with comments, folding and white space around dots removed. The display name is not part of the canonical form (`DisplayName` has it).

The domain is lowercase and in A-label form: `info@Bücher.example` gives `info@xn--bcher-kva.example` (`UnicodeDomain` shows the U-label form). Processing is UTS 46 non-transitional with `UseSTD3ASCIIRules`, `CheckHyphens`, `CheckBidi` and `CheckJoiners` on (IDNA2008 strictness, RFC 6531 section 3.3); the ß of `faß.de` is kept (`xn--fa-hia.de`). ASCII domains without `xn--` labels skip IDNA and are only lowercased and checked against the host-name rules. A domain is refused when a label is empty, starts or ends with a hyphen, holds a character a host name cannot hold, or when the last label is all digits (RFC 3696 section 2, which also refuses a bare IPv4 address). Domain literals: `[192.0.2.1]`, `[IPv6:2001:db8::1]` (hex lowercased, tag spelled `IPv6:`); a malformed IPv4 or IPv6 literal is refused (`email.invalid-address-literal`); other literal text is valid RFC 5322 and is carried with a warning (`email.not-smtp-literal`).

The canonical form parses back to itself.

## Limits (flags, not refusals)

`ExceedsLengthLimit` and a warning issue when the local part is over 64 octets (RFC 5321 section 4.5.3.1.1), the domain over 255, a label over 63 (RFC 1035 section 2.3.4), or the address over 254 octets (RFC 3696 erratum 1690). Octets are UTF-8 bytes of the canonical form. Input is limited only by memory; comment nesting is limited to 64 levels.

## Flags

| Flag | Meaning |
|---|---|
| `RoleAccount` | the unquoted local part, up to a `+`, case-folded, is on the role list: RFC 2142 names (abuse, noc, security, postmaster, hostmaster, usenet, news, webmaster, www, uucp, ftp, info, marketing, sales, support) and common contact-page names (admin, administrator, accounts, accounting, billing, careers, contact, customerservice, customer-service, customer_service, enquiries, feedback, finance, hello, help, helpdesk, hr, inquiries, jobs, legal, mail, mailer-daemon, media, newsletter, no-reply, no_reply, noreply, nobody, office, orders, payroll, press, privacy, recruiting, registrar, root, sales, security, service, spam, team) |
| `FreeProvider` | the domain is on our list of free public mailbox providers (about 90 domains: gmail, googlemail, yahoo and its country domains, ymail, rocketmail, outlook, hotmail, live, msn, aol, icloud, me, mac, proton, protonmail, pm.me, gmx, web.de, mail.com, zoho, yandex, mail.ru, fastmail, tuta, hey, qq, 163, 126, naver and others). ISP mailboxes are not on it |
| `Disposable` | the domain, or a parent of it that has at least two labels, is on the `disposable-email-domains` list (CC0, pinned in `upstream/disposable-email-domains/PIN`, compiled by `tools/Ingest.DisposableEmail`). Domains are compared as A-labels |
| `PossibleTypo` | the domain is a known mistyping of a big provider (`gmial.com`, `gmail.con`, `hotmial.com`, ...: the provider names gmail, hotmail, yahoo, outlook, icloud, live, aol, msn and protonmail with the top-level typos con, cm, om, vom, xom, cmo, ocm, cim, c0m, comm, coom, plus a list of name typos). `EmailAddress.TypoSuggestion` gives the address with the likely domain; nothing is ever changed |
| `ObsoleteSyntax`, `HasComment`, `QuotedLocalPart`, `HasDisplayName`, `DomainLiteral`, `SingleLabelDomain` | facts about the text |
| `InternationalizedLocalPart` | the local part has non-ASCII characters (delivery needs SMTPUTF8, RFC 6531) |
| `InternationalizedDomain` | the domain went through IDNA (non-ASCII, or an `xn--` label) |

## Key, `KeyVersion.V1`

`EmailAddress.Key` / `GetKey(KeyVersion.V1)` / `EmailKey.Compute`. Rules, in order:

1. **Unquote.** A local part written as one quoted string whose text (quoted-pairs resolved) is a valid dot-atom loses its quotes: `"john"@x` and `john@x` are one mailbox (RFC 5321 section 4.1.2). Otherwise the quoted form stays (and is case-folded below).
2. **Fold.** The local part is lowercased with the invariant culture and, if it has non-ASCII characters, composed to NFC (our own implementation of UAX #15 over Unicode data, so the result does not depend on the platform).
3. **Provider rules.** Only for these exact domains, and only when the local part is not left empty:

| Domains | Rule | Provider page (read 2026-11-05) |
|---|---|---|
| `gmail.com`, `googlemail.com` | drop everything from the first `+`; drop all dots; the domain becomes `gmail.com` | "Dots don't matter in Gmail addresses", support.google.com/mail/answer/7436150; "Send emails from a different address or alias", section "Use Gmail aliases", support.google.com/mail/answer/22370; gmail.com and googlemail.com being one service: Gmail Help Community, "Confused email addresses", support.google.com/mail/thread/378507106 (a community answer; Google's help centre has no page on it) |
| `outlook.com`, `hotmail.com`, `live.com` | drop everything from the first `+` | "Send email from a different address in Outlook.com", section "Using site-specific email addresses in Outlook.com", support.microsoft.com/en-us/outlook/send-email-from-a-different-address-in-outlook-com |
| `yahoo.com` | drop everything from the first `-` (`basename-keyword`) | "Create, use, edit, or delete disposable email addresses in Yahoo Mail", help.yahoo.com/kb/SLN28338.html |
| `fastmail.com` | drop everything from the first `+`; `anything@user.fastmail.com` (one subdomain level) becomes `user@fastmail.com` | "Plus addressing and subdomain addressing", www.fastmail.com/help/receive/addressing.html |

4. **Everything else** (unknown domains, domain literals): rules 1 and 2 only. The domain part of the key is the canonical domain.

iCloud has no rule: Apple's help pages do not document `+` addresses, so only rules 1, 2 and 4 apply to `icloud.com`, `me.com` and `mac.com`.

The key is `<local>@<domain>`. Re-reading a key as an address gives the same key. A published key version's output never changes: a new rule is a new `KeyVersion`.

## Reason codes

`email.missing-at` 1, `empty-local-part` 2, `empty-domain` 3, `invalid-local-part` 4, `invalid-domain` 5, `invalid-display-name` 6, `invalid-quoted-string` 7, `invalid-comment` 8, `unbalanced-angle` 9, `trailing-text` 10, `invalid-address-literal` 11, `domain-idna-failed` 12, `numeric-top-level-domain` 13, `invalid-character` 14. Issues that do not stop the parse: `local-part-too-long` 20, `domain-too-long` 21, `label-too-long` 22, `address-too-long` 23 (warnings); `obsolete-syntax` 24, `single-label-domain` 25, `possible-typo` 26 (info); `not-smtp-literal` 27 (warning). An empty input or empty `<>` is the shared `empty`.

## Offsets

`EmailAddress.Source` is the mailbox without surrounding white space. `ParseDetail.Fields` has `display-name` (from the first to the last token, quotes included), `local-part` and `domain`, as ranges of the input, decided by rules with confidence 1. A failure's issue span points at the character that stopped the parse.

## Conformance

`tests/OneContact.Tests/Email/corpus/` holds the cases (format in its README): every address example of RFC 5322 (Appendix A and the sections' grammar), RFC 3696 section 3 with its errata, RFC 2047 section 8, grammar-derived cases for RFC 6531/6532 (those RFCs print no example addresses), and a case for every provider rule and flag. The isemail list (BSD-3-Clause, pinned in `upstream/isemail/PIN`) is run through the parser with expectations from its categories; the 35 cases where our reading of the RFCs differs are listed with their clause in `isemail-overrides.cases`.
