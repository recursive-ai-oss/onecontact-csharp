# Evaluation sets and baselines

How the real-text evaluation sets for United States and Canadian addresses and names are built, labelled and scored, and how the
reference parsers are run on them. The sets themselves hold text copied from public web pages, including personal data, so they are
kept private: only the tools, the method and aggregate scores are published.

## Where the text comes from

Common Crawl, one pinned crawl (`CC-MAIN-2026-43`), through the public index and WARC byte ranges only.

1. `tools/Eval/crawl/select-pages.py` reads the crawl's `cluster.idx` and draws random index blocks (fixed seed): from `.com`, `.org`,
   `.net`, `.us`, `.edu`, `.gov`, `.biz` and `.info` hosts for the United States, from `.ca` hosts for Canada. From each block it keeps
   HTML captures with status 200 whose path suggests contact, about, team, staff or location content, or the site root (footers),
   at most two pages per host per block.
2. `fetch-pages.py` reads those captures from the WARC files and stores each page's visible lines (scripts, styles and elements marked
   hidden are skipped; block elements and `<br>` start a new line). Pages are fetched in a fixed shuffled order, politely, with
   back-off; the run is resumable.
3. `extract-candidates.py` finds address and name strings **as they appear** and draws a diverse sample.
   - A page counts as US when it holds a US state with a ZIP code, as Canadian when it holds a province with a postal code. Names are
     taken only from such pages.
   - An address candidate is the line holding the postal code plus up to three preceding lines (stopping at the street line), the
     following country line if there is one, trimmed after the postal code. Text is never edited otherwise.
   - A person candidate is a short capitalized line followed by a role line, or carrying a title or credential; a company candidate is
     a copyright-line name, a JSON-LD organisation name or the `og:site_name` value.
   - The sample is stratified by the rarest of: PO box or rural route, unit, multi-line, single line, upper case, directional, hyphenated
     number, French (Canada); persons by plain, titled, French; companies by legal form or none. At most two candidates per host.
   A second, smaller draw (`select-pages.py ... balanced`) tops up the United States pages, drawing blocks evenly across the host TLDs.
   `freeze-sets.py` then writes each gold file in canonical form (lines sorted by id, sorted keys, compact JSON) and a manifest with
   each file's role (`eval`), item count and SHA-256; training code must load only `train` and `dev` files.
4. The reviewer reads every drafted item, with the rules' pre-label beside it, and corrects it (`tools/Eval/labeling.py`). Items that are
   not an address or a name are dropped. No hosted model labels anything.

Because the rules' pre-label is the starting point of the adjudication, the rules' own scores on these sets are optimistic: items the
reviewer accepted unchanged count as agreement. Scores of the reference systems are not affected, since they never saw the pre-label.
Strings that turned out not to be addresses or names, glued page text, near-duplicates of another item and ambiguous layouts were dropped.
No several-people string survived the review in the United States and Canadian samples, so that class is not measured here.

Each gold item keeps the WARC filename, offset and length of its page, who labelled it, and whether the rules' pre-label was accepted
unchanged (`rules_agreed`).

## Label sets

### Addresses

Values are the text as written in the input, a substring (punctuation at the ends trimmed). Scoring compares them folded: accents
removed, case folded, only letters and digits kept.

| Label | Content |
|---|---|
| `recipient` | an organization, person or building name written with the address |
| `house_number` | the primary number, with a letter suffix or hyphenated form (`112-10`); in a Canadian `10-123 Main St` it is `123` |
| `road` | the street as one span: directional, name, type, directional |
| `unit` | unit designator and number, floor, suite, `bureau`, `local`, a Canadian number before the civic number |
| `po_box` | PO box, `CP`, rural or highway contract route, general delivery, with its station |
| `city`, `state`, `postcode`, `country` | as written |

Fine parts, scored only for systems that supply them: `pre_directional`, `street_name`, `street_type`, `post_directional`.

### Names

Classes: `person`, `company`, `multiple` (several people in one string), `unknown` (not a name). One label per white-space word:
`T` title, `G` given, `M` middle, `F` family (a particle belongs to the family name), `S` generational or professional suffix,
`N` nickname, `B` company base, `L` legal form, `O` other.

## Scoring

`tools/Eval/score.py` prints counts, scores and 95% intervals only.

- **Field F1** (addresses): micro-averaged over items; a predicted field is right when label and folded value equal the gold's.
- **Full-parse accuracy**: the share of items whose predicted labels and values equal the gold's exactly.
- **Token accuracy** (names): the share of words, over person and multiple-person items, whose label is right; predicted parts are
  matched to words by text, in order. Company token accuracy likewise over company items.
- **Classification accuracy**: the share of items with the right class.
- **Intervals**: percentile bootstrap over items, 1000 resamples, seed 20261105, the same resamples for every system so differences to
  the reference system are paired.

Coarse labels (the ones libpostal can produce) are scored for every system; fine rows exist for systems that report street parts.

## Reference systems

| System | Version | How it is run |
|---|---|---|
| libpostal | 1.1.4 (conda-forge binary package, MIT) with its own parser model (data v1.0.0) | `postal.parser.parse_address`, via `tools/Oracles/LibPostal` |
| libpostal with Senzing's parser model | libpostal-data v1.2.0 `parser.tar.gz` (repository licence Apache-2.0) | the same, with the parser files replaced |
| probablepeople | 0.5.6 | `tools/Oracles/Names/run-eval-names.py` |
| python-nameparser | 2.8.2 | the same; it has no classification, so only token accuracy applies |
| OneContact rules | this repository | `tools/Eval/Rules` |

The libpostal label mapping is written at the top of `run-libpostal.py`; the probablepeople and python-nameparser mappings at the top of
`run-eval-names.py`. Multi-line input reaches libpostal with its lines joined by `, `.

`tools/Eval/smoke.sh <dir>` runs the whole pipeline over the synthetic sets in `testdata/` (no real data) to check the tools.

## Fresh sets: names (US, Canada), Romanian and Mexican addresses, several-people strings

The first name sets above were used to choose a training mix (variants were scored on them), so they are retired for gating: their manifest
carries `status: touched` and the sets stay only for history. Three new sets were drawn the same way, from the same crawl, before any model was
scored on them.

| Set | Items | Source of pages |
|---|---|---|
| United States names | 271 (155 person, 112 company, 4 several people) | 110 balanced United States blocks, seed 5050 |
| Canada names | 307 (184 person, 120 company, 3 several people) | 90 `.ca` blocks, same draw |
| Romanian addresses, plus 9 several-people strings | 141 | 400 `.ro` blocks, seed 5051 |
| Mexican addresses, plus 12 several-people strings | 144 | 400 `.mx` blocks, seed 5051 |

How they differ from the first draw:

- **No host or URL is reused.** `tools/Eval/fresh/exclude.py build` lists every host and URL of every page store and exclusion list under the
  private evaluation folder (the Tier 1, extraction and all Tier 2 sets); the selection scripts skip those hosts (`EXCLUDE_HOSTS` for
  `select-pages.py`, the exclusion file argument of the Tier 2 selector). `exclude.py check` then reads each frozen set's gold items back to their
  pages and counts hosts and URLs found in the older stores or in another new set. The answer for all four new sets is 0 hosts and 0 URLs.
- **Names:** two drafts from the same page pool (`extract-candidates.py`, seeds 5052 and 5054; the second excludes every string of the first
  and the fixed cap of two persons per host applies to each draft), then the same adjudication. Items were dropped when they were page furniture,
  glued strings, a single word, an organization written as a person or an obvious duplicate; nothing was padded to reach a count.
- **Several people in one string** (`tools/Eval/fresh/extract-multi.py`): short lines of the form `A and B Family`, `Mr. & Mrs. A Family`,
  `Tom & Diane Family`, where each side opens with a title or a common given name (a short list of our own, extendable per language through
  `GIVEN_EXTRA`), so navigation labels such as "Boards & Committees" never reach the draft. Few pages write couples this way: the United
  States and Canada pages gave seven, Romanian pages nine and Mexican pages twelve, and those are all the sets hold. Labels: `G O G F` for
  `Tom & Diane Miller`-shaped strings, one label per word, `O` for the joining word. The Romanian and Mexican sets hold these strings only
  (class `multiple`); they have no person or company items.
- **Addresses (Romania, Mexico):** the Tier 2 south selector and drafter (`select-pages.py`, `extract-candidates.py`, `draw.sh`), the rules'
  hinted pre-label, then adjudication with `tools/Eval/fresh/addcorr.py`, which turns a reviewer's short list into `labeling.py` corrections.
  Conventions are those of the first Romanian and Mexican sets: the road keeps its type; `nr.` and `C.P.` words are outside the house number
  and the postal code (digits only); a county is the state without `jud.`; sector, colonia and alcaldía text is unlabelled unless it is the only
  locality; a village given with a house number and no street is the road; `bl.`, `sc.`, `ap.`, `Piso`, `Int.` go to `unit`; `C.P. n` is a
  post-office box in Romania. Near-duplicate addresses written on different pages were kept once.
- **Freezing:** `freeze-sets.py` writes each gold file in canonical form and a manifest with item counts and SHA-256 values; no model or
  reference system was run on a set before it was frozen.

`tools/Eval/fresh/score-fresh.sh` scores one set against the installed references (libpostal with its own model, libpostal with Senzing's model, probablepeople,
python-nameparser, cleanco), the rules, and the rules with the shipped Tier 1 + Tier 2 model; it prints aggregates only. The libpostal and Senzing
rows run without a country, as in the first rows of these sets; the rules and model rows are given both with and without the country hint.
