# Hand-labelled spans for the contact-span head

`docs/extraction-crawl-training.md` labels crawled pages from their own markup, and the labels turned out to say little about running text. This note is the other method: short pieces of crawled text, labelled by a reader who decides each person and organization against the evaluation convention. The tools are `crawl-spans select` and `crawl-spans apply` in `tools/data/crawl-spans`.

## Pieces

- **Pages.** The pages `crawl-spans` already holds from `CC-MAIN-2026-39` (no new fetching). Their sites were checked against every evaluation store when they were drawn (`exclude-all-stores.py`, `check-stores.py`); the check runs again over the pieces' pages before training.
- **Windows.** `select` reads each page's visible text the way extraction does and cuts windows of at most six lines and about 600 characters around the places where a decision has to be made: a markup name repeated in the text, a line shaped like a person's name, a line with a legal form, a name or organization the rules guessed, a line with a contact cue (`contact`, `call`, `owner`, `agent`, `Dr.`, …), a line holding a phone, email, web address or address, and, as look-alikes, short title-case lines that are not names (headings, product names, menu items).
- **Draw.** Windows are drawn by stratum (person, organization, fields only, cue only, look-alike) with a fixed seed, at most three per page, identical texts once. The quota and seed are in the manifest of the run.
- **Proposals.** Every window comes with proposed spans: phone, email, web address and address from the rules (the rules score above 99 on phone and email, and the model's values for those are kept only when core's parser accepts them), and persons and organizations from the markup names, the name-shaped lines, the rules' guesses and legal-form phrases. The reader sees the piece with the proposals written inline and answers with corrections only.

## Convention (fixed before the first piece was read)

This is the evaluation sets' contact convention (`docs/extraction-eval-sets.md`), so the labels agree with the rows they are measured on.

| Label | Counts | Does not count |
|---|---|---|
| person | a person named as a contact: staff, owner, agent, practitioner, instructor, the person a signature, a "contact X" line or a staff list names, the person a profile page is about | authors, bylines and commenters, reviewers and testimonial sign-offs, people only mentioned in running text (news, history, quotations), public figures, names of products or places that look like names |
| organization | the page's or text's own organization (heading, copyright line, address-block heading, every occurrence of the entity's own name) and organizations named as the party to contact | organizations only mentioned in passing, publishers and sources in citations, site-builder and theme credits, software and platform names, brands of goods on sale, social networks, schools or employers named in a biography |
| address, phone, email, website | as in the evaluation convention; the rules' proposals stand unless they are wrong | placeholders (`555-555-5555`, template addresses), the address of a place named in an event notice, a cited source |

Span shape: the person's given and family names (and middle names or initials) as written, **without** a leading title (`Dr.`, `Mr.`, `Rev.`) and without trailing credentials or suffixes (`MD`, `DDS`, `Esq.`, `Jr.`); the organization's name with its legal form (`Acme Dental LLC`), without a leading "The" only when the text writes it as a separate word of a sentence. Every occurrence in a piece is a mention and is labelled. A name that is only a first name (`call Jane`) is not labelled. When the reader cannot tell, the piece is skipped, not guessed.

A piece whose proposals are all rejected is a **negative** piece: everything outside the contact values it holds is outside a field.

## Records

Each distinct person or organization in a piece is a record; a contact value belongs to the nearest name within three lines (the earlier one when both are as near) and is otherwise unattached. The first field of each record starts a block, as in the rendered blocks.

## Corrections file

One line per piece: `c0007 -2 ; -5 ; +P:Jane Smith ; +O:Acme Dental`, `c0008 ok`, `c0009 skip`. `-N` removes proposal N as numbered in the review file, `-P`, `-O` and `-all` remove every person, organization or both, and `+X:text` labels every whole-word occurrence of `text` (P person, O organization, A address, T phone, E email, W website). `apply` writes `all.jsonl`, `dev.jsonl` (hosts hashed into 8%), `train-positive.jsonl` and `train-negative.jsonl` in the form `crawl-spans check` and `recipes/tier1 spans --crawl` read.

The labelled pieces, the review file and the corrections stay on the machine that holds the pages; only counts, hashes and scores leave it.
