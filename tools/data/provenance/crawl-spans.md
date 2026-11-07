# Provenance: crawl-trained span-head candidates

Candidates only (not packed in any package). The method is `docs/maintainers/extraction-crawl-training.md`; this file lists the inputs.

| Item | Value |
|---|---|
| Source | Common Crawl crawl `CC-MAIN-2026-39`, public index and WARC byte ranges (`data.commoncrawl.org`), evaluation use excluded from the draw |
| Terms | Common Crawl Terms of Use, last updated 2024-03-07 (read 2026-11-05 and 2026-11-07): limited licence to the crawled content; the project's training-data rule allows crawled text for training. Page text, labelled pieces and checkpoints stay on the machine that holds them |
| Credit | "Contains data from the Common Crawl corpus (commoncrawl.org)." |
| Draw | `tools/data/crawl-spans/draw.sh CC-MAIN-2026-39 5101 160 50`; 10,523 captures drawn, 10,415 after the site filter, 10,375 fetched |
| Host exclusion | every host and site named in a page store, candidate list or exclusion list under the private evaluation folder (43,804 hosts, 45,340 URLs in the page stores at the last check); result: 0 pages with a host, site or URL in it |
| Labels | the pages' own structured markup, read by the library's readers, matched to the visible text; no hosted-model label, no label from a trained model |
| Label files | `train-positive` `a488f038…`, `train-negative` `7a7d359f…`, `dev` `b831d15e…` (role `train` and `dev`; no evaluation item) |
| Recipe | `recipes/tier1` command `spans` with `--crawl`, `--crawl-negative`, `--crawl-dev`; OneNLP 0.2.0-preview.1, OneModel 1.0.0, seed 1, CPU |
| Candidate A | positives ×6, negatives ×1; weights `tier1.onenlp` 10,197,617 bytes, SHA-256 `06c96bfb…2de3` |
| Candidate B | positives ×15, negatives ×2; weights 10,197,617 bytes, SHA-256 `0a94a931…730b` |
| Evaluation | each candidate scored once on the private extraction sets and on development examples |
