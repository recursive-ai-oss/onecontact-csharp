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
