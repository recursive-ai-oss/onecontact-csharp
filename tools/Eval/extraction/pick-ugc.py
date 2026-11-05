#!/usr/bin/env python3
"""Step 3 (UGC set): cuts free-text items out of the fetched forum, classified, comment and blog pages and draws about 150.

usage: pick-ugc.py <set-dir> <seed> [total]

An item is a run of one to six consecutive visible lines of one page (a post, a comment, an ad, a signature block), at most 900
characters, with the lines joined by newlines; nothing is edited. A run that is mostly short menu-like lines is not an item, and
neither is a run of prose that does not look English (the sets are for North American text; French is kept on .ca hosts). Candidate windows are found around lines that carry a contact
signal; a few windows with no signal at all are kept as negatives. Strata, drawn rarest first, at most one item per host:
  address    a street line or a ZIP / postal code
  phone      a North American phone number
  email      an email address, written out or obfuscated
  website    a URL or a bare domain
  signature  "my name is", "I am", a sign-off name or a company line, with none of the above
  mixed      two or more of the signals above
  none       no signal
Writes <set-dir>/ugc-items.jsonl: id, country, text, the WARC reference, a hash of the page URL and the stratum.
"""
import hashlib
import json
import os
import random
import re
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "crawl"))
import cc  # noqa: E402

d, seed = sys.argv[1], int(sys.argv[2])
total = int(sys.argv[3]) if len(sys.argv) > 3 else 150
pages = [json.loads(l) for l in open(os.path.join(d, "pages.jsonl"), encoding="utf-8")]

PHONE = re.compile(r"(?<!\d)(?:\+?1[-. ]?)?\(?\d{3}\)?[-. ]\d{3}[-. ]\d{4}(?!\d)")
EMAIL = re.compile(r"[\w.%+-]+\s?(?:@|\[at\]|\(at\)| at )\s?[\w-]+(?:\.|\s?\[dot\]\s?|\s?\(dot\)\s?)[A-Za-z]{2,}")
URL = re.compile(r"(?:https?://|www\.)\S+|\b[a-z0-9-]+\.(?:com|org|net|ca|us|info|biz)\b/?\S*", re.I)
ADDR = re.compile(r"\b\d{1,5}\s+[A-Z0-9][\w.]*(?:\s+[\w.]+){0,3}\s+(?:St|Street|Ave|Avenue|Rd|Road|Blvd|Boulevard|Dr|Drive|Ln|Lane|Way|Hwy|Court|Ct|Pkwy)\b\.?|"
                  r"\b[A-Z]{2}\s+\d{5}(?:-\d{4})?\b|\b[A-Z]\d[A-Z]\s?\d[A-Z]\d\b|\bP\.?O\.? Box \d+", re.I)
SIGN = re.compile(r"\b(?:my name is|I am|I'm|this is|call me|ask for|contact|thanks,|regards,|sincerely,|cheers,)\s+[A-Z][a-z]+|^[-\u2014~]\s*[A-Z][a-z]+(?: [A-Z][a-z]+)?$|\b(?:Inc|LLC|Ltd|Corp)\b", re.M)
SIGNALS = {"address": ADDR, "phone": PHONE, "email": EMAIL, "website": URL, "signature": SIGN}

STOP = set("the and to of a is you for in on at call email contact our we your with this that are it as be or from by have will can please".split())
FRENCH = set("le la les des et de du un une pour vous nous est dans sur au aux avec votre notre".split())


def prose_ok(text, tld):
    words = re.findall(r"[A-Za-z\u00c0-\u00ff']+", text.lower())
    if len(words) < 6:
        return True  # too short to judge: a bare phone line or address
    en = sum(w in STOP for w in words) / len(words)
    fr = sum(w in FRENCH for w in words) / len(words)
    return en >= 0.08 or (tld == "ca" and fr >= 0.08)


def menu_like(parts):
    short = sum(len(x) < 18 for x in parts)
    return len(parts) >= 3 and short / len(parts) > 0.6


rng = random.Random(seed)
rng.shuffle(pages)
windows = []
for p in pages:
    raw = open(os.path.join(d, "pages", p["id"] + ".html"), "rb").read()
    lines = cc.page_lines(cc.decode(p["ctype"], raw)).lines
    used_to = -1
    for i, line in enumerate(lines):
        if i <= used_to or len(line) < 12:
            continue
        # extend the window over neighbouring short lines (a signature or address block is several lines)
        lo, hi = i, i
        while hi - lo < 5 and hi + 1 < len(lines) and len(lines[hi + 1]) < 140 and sum(len(x) for x in lines[lo:hi + 2]) < 900:
            hi += 1
        text = "\n".join(lines[lo:hi + 1])
        if len(text) < 20 or len(text) > 900 or sum(ord(c) > 127 for c in text) > 0.1 * len(text):
            continue
        hits = {k for k, rx in SIGNALS.items() if rx.search(text)}
        if not hits and rng.random() > 0.05:
            continue
        if menu_like(lines[lo:hi + 1]) and not (hits - {"signature"}):
            continue
        if not prose_ok(text, p["tld"]) or (not (hits - {"signature"}) and len(text) < 80):
            continue
        real = hits - {"signature"}
        stratum = "mixed" if len(real) >= 2 else (next(iter(real)) if real else ("signature" if hits else "none"))
        windows.append({"page": p, "text": text, "stratum": stratum})
        used_to = hi
        if sum(1 for w in windows if w["page"] is p) >= 4:
            break

quota = {"address": 0.10, "phone": 0.17, "email": 0.17, "website": 0.08, "signature": 0.15, "mixed": 0.23, "none": 0.10}
chosen, hosts = [], set()
rng.shuffle(windows)
for stratum, share in quota.items():
    want = round(total * share)
    for w in windows:
        if want == 0:
            break
        if w["stratum"] == stratum and w["page"]["host"] not in hosts:
            chosen.append(w)
            hosts.add(w["page"]["host"])
            want -= 1
    print(stratum, "short by", want)
for w in windows:  # top up from the unused windows when a stratum ran short
    if len(chosen) >= total:
        break
    if w not in chosen and w["stratum"] != "none" and w["page"]["host"] not in hosts:
        chosen.append(w)
        hosts.add(w["page"]["host"])
items = []
for w in chosen:
    p = w["page"]
    uh = hashlib.sha1(p["url"].encode()).hexdigest()[:12]
    items.append({"id": hashlib.sha1((uh + w["text"]).encode()).hexdigest()[:12], "country": "CA" if p["tld"] == "ca" else "US", "text": w["text"],
                  "warc": p["warc"], "page": uh, "stratum": w["stratum"]})
items.sort(key=lambda i: i["id"])
with open(os.path.join(d, "ugc-items.jsonl"), "w", encoding="utf-8") as fo:
    for it in items:
        fo.write(json.dumps(it, ensure_ascii=False) + "\n")
print("items", len(items), "CA", sum(i["country"] == "CA" for i in items))
