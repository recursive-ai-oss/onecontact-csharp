#!/usr/bin/env python3
"""Turns the visible text of crawled pages into background pages for the block generator.

usage: crawl-negatives.py <pages-dir> <visible.jsonl> <out.jsonl>

<pages-dir> is a folder written by tools/Eval/extraction/select.py and fetch.py (pages.jsonl with each page's host and WARC reference);
<visible.jsonl> is the output of `eval-extract visible` over it ({"id", "text"}: the lines our reader sees). A page is kept only when
no line of it looks like contact data at all (no @, no web address, no phone-like or ZIP-like number, no street-and-number line), so
that every line of a kept page can be used as a line that carries no field. Pages that mention contact data are dropped whole:
dropping only the matching lines would leave the people and companies next to them unlabelled.

Output: one JSON object per kept page, {"host", "lines": [...]}, lines of 3 to 500 characters, at most 300 per page.
"""
import json
import re
import sys

pages_dir, visible, out = sys.argv[1:4]

CONTACT = re.compile(
    r"@|https?://|www\.|\b[a-z0-9-]+\.(com|ca|org|net|edu|gov|us|info|biz|io|co)\b"
    r"|\(?\d{3}\)?[\s.\-]\d{3}[\s.\-]\d{4}|\+\d[\d\s().\-]{7,}|\b\d{3}[\s.\-]\d{4}\b"
    r"|\b\d{5}(-\d{4})?\b|\b[A-Z]\d[A-Z] ?\d[A-Z]\d\b"
    r"|\b\d{1,5}\s+[A-Z][\w.]*(\s+[A-Z][\w.]*)?\s+(St|Street|Ave|Avenue|Rd|Road|Blvd|Boulevard|Dr|Drive|Ln|Lane|Way|Ct|Court|Hwy|Highway|Pkwy|Suite|Ste)\b"
    r"|\b(tel|phone|fax|email|e-mail|courriel|téléphone)\s*[:.]",
    re.IGNORECASE,
)

host_of = {}
for line in open(f"{pages_dir}/pages.jsonl", encoding="utf-8"):
    j = json.loads(line)
    host_of[j["id"]] = j["host"]

kept = dropped = 0
with open(out, "w", encoding="utf-8") as fo:
    for line in open(visible, encoding="utf-8"):
        j = json.loads(line)
        lines = [l.strip() for l in j["text"].split("\n")]
        lines = [l for l in lines if 3 <= len(l) <= 500]
        if len(lines) < 8 or any(CONTACT.search(l) for l in lines):
            dropped += 1
            continue
        fo.write(json.dumps({"host": host_of[j["id"]], "lines": lines[:300]}, ensure_ascii=False) + "\n")
        kept += 1
print("pages kept", kept, "dropped", dropped)
