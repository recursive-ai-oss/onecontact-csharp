# Crawl-trained contact-span head: how the labels are made

The contact-span head (`docs/extraction.md`, "The span model") can be trained on real web pages as well as on the rendered blocks. This note is the method of `tools/data/crawl-spans`: where the pages come from, how a page labels itself, which pieces are kept and which are thrown away, and how the result is mixed into training. A model trained this way is a candidate: it is measured against the rendered-block model on the private extraction sets and ships only if it wins.

## Pages

- **Source.** Common Crawl, one crawl that is not the one the evaluation sets were drawn from (`CC-MAIN-2026-39`; the sets use `CC-MAIN-2026-43`), read through the public index and WARC byte ranges only (`tools/Eval/crawl/cc.py`). `draw.sh` draws random blocks of the crawl's `cluster.idx` with a fixed seed from United States hosts (`.com .org .net .us .edu .gov .biz .info`) and `.ca` hosts, keeps HTML captures with status 200 (at most two per host per block), and fetches them with two threads and back-off on throttling. About five in six captures have contact, about, team, staff or location paths; the rest are any other page.
- **Hosts left out.** `exclusions.py` walks the private evaluation folder and collects every host named in a candidate list, page list or exclusion file below it (the Tier 1 address and name sets, the extraction sets, every Tier 2 set, the replacement name sets) plus the earlier span-head drafts' evaluation hosts. A candidate is dropped when its host *or its site* (the last two labels of its host name) is on that list, so a sibling sub-domain of an evaluation host is gone too. `check-overlap.py check` runs again over the fetched pages and must print 0.
- **Terms.** Common Crawl's terms of use (read 2026-11-05 and 2026-11-07) allow the crawled content to be used under a limited licence; the project's training-data rule allows crawled text for training its models. Page text stays on the machine that holds the pages: the pages, the labelled pieces and the checkpoints are never put in the repository, a package, a log or a document. The weights contain no page text as such. What leaves the machine are counts and scores.

## A page labels itself

For each page, `crawl-spans label`:

1. Reads the structured markup with the library's own readers (JSON-LD, microdata, RDFa, h-card, `mailto:` and `tel:` links; `docs/extraction.md`). Each markup record has a name, its kind (person or organization) and the phone, email, website and address values it holds.
2. Reads the page's visible text the way extraction does at run time (the same cascade-visible lines).
3. Finds every whole-word, same-line occurrence of a markup name in the visible text. A person's name is kept as a label only when it has two to five words that look like a name; an organization's name when it is at least three characters. Every occurrence is a mention, as in the evaluation convention (a company's name in the header and again in the footer are two labelled spans). A markup name that does not appear in the visible text gives nothing: hidden text is not a training example.
4. Runs the rules over the visible text for the other fields. A rule phone, email, address or website is a label (the rules score 99+ on phones and emails and are the model's own gate: the model's value of those fields is kept only if the core parser accepts it). A name the rules *guessed* is **not** a label: on real pages the rules over-read names, so it only marks a place where the page may hold a person nobody labelled (below).
5. Marks which rule fields the markup *explains*: an email, a phone number (last ten digits) or an address (half of its tokens in the markup's address values) that the markup also states. A rule field the markup does not state is unexplained.

## When the rest of a piece may count as empty

A span-labelling example says that everything outside its spans is not a field, so a piece may be used only where the markup is complete enough to say that. Pieces are cut exactly as the model reads text at run time (up to 40 lines or 3,000 characters), plus short windows (the record's lines and two lines each side, at most 12 lines) around each markup record. A piece is **dropped** when:

- it holds a rule email, phone or address that the markup does not explain (a person or company may sit next to it);
- it holds contact data and no labelled name at all (the markup knows a name there that did not appear in the text);
- a name the rules guessed sits within three lines of any contact data;
- it has lines that look like names (a given name from the library's list, then one or two capitalized words) that the markup did not label, when it has no labelled name or has three or more such lines (a staff list the markup does not cover);
- it holds an unlabelled capitalized phrase ending in a legal form or a business word (`Inc`, `LLC`, `Associates`, `Realty`, `Clinic`, ...): an organization the page names whether or not the markup does.

What is kept:

- **positive pieces**: at least one labelled person or organization of a markup record, with the record's phone, email, address and website mentions labelled too. The first field of each markup record starts a block, as in the rendered blocks; rule fields near a markup name join its record;
- **negative pieces**: pieces with no labelled name that passed the filters above (navigation, prose, product pages, forms, listings): every word is outside a field, except the explained contact values and web addresses inside them.

The labels are weak. What they cannot say, they leave out (the drops above), and what remains inherits the markup's choices: markup names a page's own organization and its staff, which is the evaluation's contact convention, and not the people mentioned in running text.

## Split and mixing

- The split is by host: a stable hash of the host name puts 5% of hosts in dev, the rest in train. No host is in both (`crawl-spans check` verifies).
- `recipes/tier1 spans --crawl FILES --crawl-repeat N --crawl-dev FILES` mixes the pieces into the rendered blocks. The crawl set is small, so each training piece is repeated N times and shuffled with a fixed seed; the held-out crawl pieces are added to the development examples. Without `--crawl` the recipe is unchanged.
- The model is selected on development examples only (rendered blocks and crawl pieces), never on the evaluation sets.

## Reproducing

```
tools/data/crawl-spans/draw.sh CC-MAIN-2026-39 5101 160 50 $WORK/cc/web      # pages (resumable)
dotnet crawl-spans.dll label --pages $WORK/cc/web --out $WORK/labels/v1       # train.jsonl, dev.jsonl
dotnet crawl-spans.dll check --in $WORK/labels/v1/train.jsonl $WORK/labels/v1/dev.jsonl
```

## First run and result

Data (`tools/data/crawl-spans`, labels `v1`): crawl `CC-MAIN-2026-39`, block-selection seed 5101 (160 United States and 50 `.ca` index blocks), 10,415 captures kept after the site filter (108 dropped), 10,375 pages fetched (1.4 GB, not in any repository), 9,959 distinct pages labelled; 3,479 had a markup record and 1,985 a markup name found in their text. Check against every evaluation page store and exclusion list (`check-stores.py`, including the replacement name sets and the Romanian and Mexican address top-ups): 0 pages with an excluded host, site or URL.

| File | Pieces | SHA-256 |
|---|---|---|
| `train-positive.jsonl` | 2,769 (2,021 pieces, 748 windows) | `a488f038…8143` |
| `train-negative.jsonl` | 14,606 | `7a7d359f…2e9` |
| `dev.jsonl` | 755 (123 positive, 632 negative) | `b831d15e…8366` |

Training spans: 5,227 organization and 1,135 person mentions from markup, 17,019 web addresses and 458 phone, email and address mentions (263 from markup, 195 from the rules). `crawl-spans check` finds 0 bad spans and 0 hosts in both files.

Two candidates, `recipes/tier1 spans` on the Tier 1 encoder (frozen, seed 1, 4 epochs, 400,000 rendered blocks): **A** repeats each positive piece 6 times and each negative once, **B** 15 and 2 times. The best development epoch was kept (the second in both; later epochs fit the repeated pieces and lose on dev).

| | rendered-block dev (field F1 / block-start F1) | held-out crawl pieces: person F1 / organization F1 |
|---|---|---|
| shipped head | 0.9324 / 0.8861 | 0.012 / 0.053 |
| A | 0.9353 / 0.8694 | 0.000 / 0.023 |
| B | 0.9322 / 0.8660 | 0.000 / 0.044 |

On pages from hosts it never saw, no head finds the markup's people and organizations: the label says *this phrase is the page's organization*, and nothing in the text of a footer or a heading tells that apart from other capitalized phrases. Weak labels of this kind teach the training pieces, not a rule that carries over.

On the private extraction sets (value F1 of visible-text mentions, 1000-resample bootstrap, differences paired with the shipped configuration):

| Row | shipped | A | B | rules only |
|---|---|---|---|---|
| text person | 34.8 | 36.4 (+1.6 [−1.4, +5.5]) | 35.6 (+0.8 [−2.8, +4.8]) | 40.0 (+5.2 [−0.8, +12.3]) |
| text org | 55.6 | 55.9 (+0.3 [−5.5, +4.7]) | 57.6 (+2.0 [−3.8, +6.8]) | 60.3 (+4.8 [−1.5, +10.4]) |
| text address | 71.7 | 61.5 (−10.2 [−25.9, +3.3]) | 58.3 (−13.4 [−28.3, −2.0]) | 46.2 |
| text record F1 | 31.5 | 32.1 (+0.6) | 32.1 (+0.6) | 31.7 |
| web person | 19.4 | 19.4 (0.0) | 19.4 (0.0) | 17.9 |
| web org | 35.9 | 35.9 (0.0) | 35.9 (0.0) | 36.7 |
| web address | 84.0 | 84.0 | 84.0 | 84.0 |
| web record F1 | 25.4 | 25.4 (0.0) | 25.4 (0.0) | 24.6 |

Letting either head read web pages (the gated mode) changes web person by −0.3, record F1 by −0.1 and nothing else, as the shipped head does by −1.4 and −1.3. Phone, email and website rows are unchanged in every configuration.

Verdict: neither candidate is better than the shipped head. The person and organization gains on text are inside the noise, and text addresses fall (B beyond the interval). The shipped head and its wiring stay. The address, name-part and name-class heads are byte-identical in effect: the same scores on 7,899 held-out address and name examples for all three files.
