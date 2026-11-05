# Address expansion dictionaries

The expansion of an address (`AddressExpander`) and the words of its key use the dictionary files of libpostal (MIT licence), ingested from pinned data files by `tools/Ingest.LibPostalDictionaries`. Only the files named in `upstream/libpostal-dictionaries/PIN` are used: English and French street types, directionals, unit and level words, building and post office words, qualifiers, synonyms, street-name words, stopwords, number words, near and cross-street words, entrances and staircases, the French elisions, and the language-independent ambiguous-expansions list. The pin records the upstream tag (v1.1.4, commit ffafa3fcd687631955453df70906c53d5677ce9a) and the SHA-256 of every file; `tools/fetch/libpostal-dictionaries.sh` fetches them by exact path into `$ONECONTACT_UPSTREAM/libpostal-dictionaries` and checks the hashes, and the ingest tool checks them again before converting anything. Nothing but data files and libpostal's published README was read.

## Upstream format

One entry per line, the canonical phrase first, then its variants, separated by `|` (`drive|dr`, `street|st|str`). Lines may lack a final line feed.

## Compiled tables

The tool writes one table per file, named `<language>/<dictionary>` (`en/street_types`, `fr/directionals`, `all/ambiguous_expansions`), into the `libpostal-dictionaries` compiled data file (format in architecture.md). A table is UTF-8 text, one entry per line, phrases separated by a tab, in upstream order. Phrases are lower-cased with the invariant culture, runs of white space become one space, empty phrases and repeated phrases within an entry are dropped, and entries left empty are dropped. The same pinned inputs give byte-identical tables.

At first use the library reads the tables into a phrase index (a hash of language and phrase to the entries holding it, with the longest phrase length for matching longest first). The packed notice is `notices/libpostal-dictionaries.notice.txt` (MIT licence and copyright line).

## How expansion uses them

Text is lower-cased, punctuation becomes a space (a period inside `p.o.` vanishes, a hyphen between digits stays), and the longest phrase at each word is looked up. Street types expand except as the first word of an English address; numbered unit and level words and `number` expand only when a number-like word follows; synonym and street-name entries expand only when the word is not already the canonical form. A word the ambiguous list names, and any single letter, is also kept as written, so `w` gives both `west` and `w`. Ordinals give the digits with and without their ending. The first variant has everything expanded, the last is the input as normalized; the rest are the combinations in between, up to the limit.
