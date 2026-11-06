#!/usr/bin/env python3
"""Step 2: reads the chosen captures from the WARC files by byte range and stores each page's visible lines.

usage: fetch-pages.py <out-dir> [max-pages] [threads]

Reads <out-dir>/candidates.jsonl, writes <out-dir>/pages.jsonl (url, WARC reference, visible lines, a few meta values, JSON-LD
organisation/person names). Resumable: pages already in pages.jsonl are skipped.
"""
import json
import os
import random
import re
import sys
import threading
from concurrent.futures import ThreadPoolExecutor

import cc

out = sys.argv[1]
limit = int(sys.argv[2]) if len(sys.argv) > 2 else 10**9
threads = int(sys.argv[3]) if len(sys.argv) > 3 else 4
cands = [json.loads(l) for l in open(os.path.join(out, "candidates.jsonl"), encoding="utf-8")]
done = set()
pp = os.path.join(out, "pages.jsonl")
if os.path.exists(pp):
    for l in open(pp, encoding="utf-8"):
        done.add(json.loads(l)["url"])
todo = [c for c in cands if c["url"] not in done]
random.Random(3).shuffle(todo)  # a fixed order that mixes countries, so a partial run is still representative
todo = todo[:limit]
lock = threading.Lock()
fo = open(pp, "a", encoding="utf-8")
count = [0, 0]


def ld_names(blobs):
    names = []
    def walk(o):
        if isinstance(o, list):
            for x in o:
                walk(x)
        elif isinstance(o, dict):
            t = o.get("@type")
            t = t if isinstance(t, list) else [t]
            if o.get("name") and isinstance(o["name"], str) and any(isinstance(x, str) and re.search(r"Organization|Business|Person|Store|Restaurant|Clinic|Dentist|Attorney|Service|Hotel|Physician", x) for x in t):
                names.append({"type": ",".join(x for x in t if isinstance(x, str)), "name": o["name"]})
            for v in o.values():
                if isinstance(v, (list, dict)):
                    walk(v)
    for b in blobs:
        try:
            walk(json.loads(b))
        except Exception:
            pass
    return names


def work(c):
    raw = cc.get(cc.DATA + c["filename"], (c["offset"], c["length"]))
    r = cc.warc_body(raw) if raw else None
    with lock:
        count[0] += 1
        if count[0] % 100 == 0:
            print("fetched", count[0], "stored", count[1], flush=True)
    if not r:
        return
    text = cc.decode(*r)
    p = cc.page_lines(text)
    if len(p.lines) < 3:
        return
    rec = {"url": c["url"], "host": c["host"], "tld": c["tld"], "languages": c["languages"], "warc": [c["filename"], c["offset"], c["length"]],
           "lines": p.lines[:1500], "meta": p.meta, "ld": ld_names(p.ldjson)}
    for k in ("country", "territory"):  # set by the Tier 2 selection
        if k in c:
            rec[k] = c[k]
    with lock:
        fo.write(json.dumps(rec, ensure_ascii=False) + "\n")
        fo.flush()
        count[1] += 1


with ThreadPoolExecutor(threads) as ex:
    list(ex.map(work, todo))
print("done", count)
