# Public Suffix List

`RecursiveAI.OneContact.Web.PublicSuffixList` answers two questions about a domain name: its public suffix and its registrable domain.

## Data

- `public_suffix_list.dat` ships **unmodified**, byte for byte as published (pinned in `upstream/psl/PIN`: publicsuffix/list commit `aae37acca5d0f3d09e7c1fe4da9024c514674ac9`, SHA-256 `762b6cbd...3430`), packed under `notices/psl/` in the package with its MPL-2.0 licence text. Source Code Form is subject to MPL-2.0; the file is never edited, and our additions live in a separate file (`src/OneContact/Web/Data/psl-additions.txt`, same syntax, read after the list, counted as private-section rules; empty for now).
- The ingest tool `tools/Ingest.Psl` compiles the same pinned file at build time into the table `rules`: one rule per line, sorted ordinally, as `[N|W|E][I|P]<rule>`:
  N plain rule, W wildcard rule (`*.rule`, stored without the star), E exception rule (`!rule`, stored without the bang); I and P say whether the rule is in the ICANN or the private section. Rules whose labels are not ASCII are stored in their `xn--` form (Punycode, RFC 3492), so lookups only compare ASCII. Each line of the list is read up to its first white space, as the list says. The same pinned inputs give the same bytes.
- `tests/test_psl.txt` from the same commit (CC0) is run by `PublicSuffixListTests`: all 78 checks pass.

## Algorithm

The list's published algorithm (publicsuffix.org/list, "Formal algorithm"): the labels of the domain are matched against every rule from the right; a wildcard label matches any one label; if several rules match, an exception rule prevails, otherwise the rule with the most labels; if none matches the rule is `*`; an exception's public suffix drops its leftmost label; the registrable domain is the public suffix plus the label in front of it.

- Input may be Unicode or `xn--` and any letter case; labels are mapped with UTS #46 so `Bücher` and `xn--bcher-kva` find the same rule. The answer is lowercase and keeps the script the input used.
- A name that is empty, starts or ends with a dot, or has an empty label has no answer (`null`).
- `includePrivateSection: false` ignores the private section, so `user.github.io` has the public suffix `io`.
- Lookups look at no more labels than the longest rule has, so a host with thousands of labels costs no more than a short one.
