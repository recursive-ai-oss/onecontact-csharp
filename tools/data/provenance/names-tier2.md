# Tier 2 name and company examples (`names-tier2-v1`)

Generator `tools/data/names-tier2` (seed 20261107), sharing its templates and noise with `tools/data/names`. 289,093 examples in all: 132,464 training persons, 123,948 training companies, 16,981 multiple-person strings, 657 unknown-class strings, plus a development split fixed by a hash of the name before rendering.

- **Persons: our own lists.** `lists.tsv` holds, for each Tier 2 language, lists of common given names (male and female), family names, titles, particles, professional suffixes, joining words ("und", "en", "e") and strings that are neither person nor company. We wrote them ourselves from general knowledge; about 20 to 40 items per kind and language. Given and family names are drawn independently, so no generated string is a real person's name. No third-party name list is used for these languages, so no licence applies. English, French and Spanish persons come from `names-us-ca-v1` (Census, SSA, Retraite Québec, INSEE).
- **Candidate open sources, not used and not licence-checked:** Poland dane.gov.pl name lists, the Czech Ministry of the Interior name statistics, Spain's INE, Italy dati.gov.it, Statistics Norway, Meertens (NL).
- **Companies: GLEIF Level 1 golden copy of 2026-11-05** (CC0, sole proprietors dropped), 33 country codes, at most 7,000 per country, the legal form located at the end of the name through the ISO 20275 list v1.6 (CC0). Records per country: 7,000 for AT AU CH DE DK EE ES FI HU IT JE NL NO PL SE; 6,252 LI; 4,974 RO; 4,944 MT; 4,213 IS; 3,878 SI; 2,865 BE; 1,957 LU; 1,128 LT; 1,089 LV; 596 CZ; 505 HR; 182 GG; 135 SK; 49 MC; 48 MX; 8 IM; 6 SM; 4 PT. No Companies House or EDGAR rows.
- Attribution: GLEIF, CC0 1.0 (credit given anyway).

# Tier 2 person names from open national lists (`names-tier2-2`)

Generator `tools/data/names` (`render-tier2`, tool tag `names-2`), seed 20261107, 1,500,000 person, 160,000 several-people and 100,000 non-name attempts. It replaces the person part of `names-tier2-v1` (above, whose companies are unchanged and still used). Run: `tools/data/names/fetch.sh tools/data/names/pins/names-tier2.pin`, then `dotnet run -c Release --project tools/data/names -- render-tier2 --data <names folder> --out <out>`. The manifest (`manifest.json`) lists every input file with size and SHA-256, the SHA-256 of each template file and of each output file, and the examples per language, country and pattern. A second run gave a byte-identical manifest and byte-identical output files.

- **Counts.** 1,556,803 training and 86,367 development examples (persons, several-people strings and non-names; no companies). Per language-country unit, training / development: DE 116,739 / 6,533; AT 58,187 / 3,225; CH-German 66,705 / 3,705; LI 8,433 / 427; NL 82,783 / 4,610; BE-Dutch 66,369 / 3,787; BE-French 33,197 / 1,851; FR 101,301 / 5,595; CH-French 25,333 / 1,411; LU 8,442 / 449; MC 5,048 / 289; IT 38,007 / 1,985; ES 74,328 / 4,175; MX 28,299 / 1,583; Catalan 15,368 / 856; PT 43,357 / 2,523; MT 7,735 / 380; GB 114,938 / 6,468; IE 49,205 / 2,721; AU 65,221 / 3,663; JE 6,614 / 332; GG 4,917 / 301; IM 4,937 / 244; DK 64,126 / 3,532; SE 66,575 / 3,659; NO 66,085 / 3,641; FI 67,110 / 3,754; IS 6,589 / 290; EE 8,163 / 416; LV 25,224 / 1,303; LT 8,408 / 531; PL 117,748 / 6,521; CZ 13,558 / 736; SK 10,165 / 505; SI 42,031 / 2,289; HR 11,367 / 637; HU 11,321 / 839; RO 12,870 / 601. The split is a hash bucket of the rendered text (90 % train, 5 % dev; buckets 95 to 99, 112,242 candidate strings, are reserved and never rendered, so evaluation sets can be drawn from them). 458,395 duplicate texts were dropped.
- **Name lists are aggregate counts** (how many living people or births carry a name), not records of people. Given and family names are drawn independently by frequency, so a generated string is not a real person's name. Titles, particles and joining words per language come from `templates/forms-tier2.tsv` (ours).

## Sources and terms (each read at the publisher on 2026-11-06; the quote is in `tools/data/names/licences/<cc>.txt`, packed credit lines in `THIRD-PARTY-NOTICES.md`)

| Source | Use | Terms as read | Verdict |
|---|---|---|---|
| PL: Ministerstwo Cyfryzacji, PESEL name statistics (dane.gov.pl datasets 1667 and 1681, 2026-01-20) | PL given and family names, feminine and masculine lists paired by sex | API field `license_name`: "CC0 1.0" | OK, no conditions |
| FI: DVV, Väestötietojärjestelmän suomalaisten nimiaineistot, 2026-08-04 | FI | "lisenssillä Creative Commons Attribution 4.0 International License" | OK, CC BY 4.0, credit |
| LV: PMLP, Personu vārdi, 2026-07-01 (data.gov.lv) | LV given names | "Licence CC0-1.0" | OK |
| IE: CSO, VSA50, VSA60, VSA110 (data.gov.ie) | IE given names and surnames (surnames mixed with GB) | "CC-BY-4.0 Creative Commons Attribution 4.0" | OK, credit |
| BE: Statbel first names 2026 (M, F) and family names 2026 | BE Dutch and French; NL (proxy) | "published under the Creative Commons Attribution 4.0 license (CC BY 4.0)" | OK, credit |
| AT: Statistik Austria, OGDEXT_VORNAMEN_1 | AT, DE given names | "subject to a Creative Commons Attribution 4.0 International (CC BY 4.0) licence ... Data source: Statistics Austria - data.statistik.gv.at" | OK, credit |
| CH: FSO last names by canton 2024 (opendata.swiss) | CH surnames; also DE, AT, LI, FR-CH surnames (proxy) | terms_by: "You may use this dataset for commercial purposes. You must provide the source" | Accepted (explicit open terms, no licence agreement to sign), credit |
| CH: Statistik Stadt Zürich, BEV370OD3701 | CH, DE, AT, LI given names | "Lizenz Creative Commons CCZero" | OK |
| DE: Rostock registry office, newborn first names 2025 | DE given names | "Lizenz CC0 1.0" | OK |
| GB: ONS baby names 2025 and NRS most common surnames 2025 | GB, JE, GG, IM, IE, AU | "Open Government Licence v3.0" | OK, OGL credit |
| AU: NSW BDM popular baby names 1952 to 2025 | AU given names | catalogue licence cc-by | OK, credit |
| NO: SSB tables 10501 and 12891 (2025) | NO | "the APIs use the license CC BY 4.0" | OK, credit |
| SE: SCB BE0001FNamn10 and ENamn10 (2020) | SE | "Vi använder licensen CC0 för dessa data" | OK |
| SI: SURS SiStat tables 05X1005S, 05X1010S, 05X1015S to 05X1018S (2026) | SI | free use, including commercial and derivative, "on condition that ... SURS is acknowledged as their source" | Accepted (not a named licence; effect as CC BY), credit |
| DK: Statistics Denmark name lists 2020 via Digitaliseringsstyrelsen | DK; the lists carry only the order, so names are weighted by rank (Zipf) | Read Me.txt: "Licens: CC 4.0 BY ... (Danmarks Statestik angives som kilde)" | OK, credit |
| FR (and LU, MC, FR-CH proxy): INSEE | FR given and family names | Licence Ouverte 2.0 (logged in `names-us-ca.md`) | OK, credit |

No licence ambiguity remains. File hashes and sizes are in `tools/data/names/pins/names-tier2.pin`. The fetcher sends a browser-like user agent (Statbel and the Finnish portal each reject one of two), so a failed fetch is retried with the other.

## Proxies and gaps

- **Proxies.** DE and AT surnames: the Swiss FSO list (no German or Austrian list found). NL: the Belgian Statbel lists plus 30 given and 30 family names of our own. LU and MC French names: INSEE. AU surnames: GB and IE lists. JE, GG, IM: GB and IE lists.
- **Our own short lists** (`tools/data/templates/tier2*-names.tsv`, about 25 given and 30 to 40 family names a language) remain for CZ, SK, HR, HU, RO, PT, ES, MX, IT, MT, CA (Catalan), EE, LT and IS: no open list with permissive terms was found (MVČR no longer publishes; the Spanish INE offers a web application only; Italian lists exist for single municipalities; Registrų centras and the Estonian register offer no bulk file; the Icelandic permitted-names list has no counts). The several-people, title and joining-word forms are ours for every language.
- **Not used:** the Luxembourg CC0 file (about 60 first names); regional Spanish lists (Navarra, Valencia, Andalucía) for lack of a bulk file.
