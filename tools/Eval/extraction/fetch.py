#!/usr/bin/env python3
"""Step 2: reads the drawn captures from the WARC files by byte range and stores each page's response body as received.

usage: fetch.py <out-dir> [max-pages] [threads] [tld]

Reads <out-dir>/candidates.jsonl; writes <out-dir>/pages/<id>.html (the HTTP body bytes, unchanged: offsets in the gold labels count
bytes of this file) and appends one line per page to <out-dir>/pages.jsonl (id, url, host, tld, content type, WARC reference, size).
With a tld, only captures from hosts of that top-level domain are read (a top-up for one country). Resumable. Polite: few threads, back-off in cc.get.
"""
import hashlib
import json
import os
import random
import sys
import threading
from concurrent.futures import ThreadPoolExecutor

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "crawl"))
import cc  # noqa: E402

out = sys.argv[1]
limit = int(sys.argv[2]) if len(sys.argv) > 2 else 10**9
threads = int(sys.argv[3]) if len(sys.argv) > 3 else 4
os.makedirs(os.path.join(out, "pages"), exist_ok=True)
cands = [json.loads(l) for l in open(os.path.join(out, "candidates.jsonl"), encoding="utf-8")]
meta_path = os.path.join(out, "pages.jsonl")
done = set()
if os.path.exists(meta_path):
    done = {json.loads(l)["url"] for l in open(meta_path, encoding="utf-8")}
tld_filter = sys.argv[4] if len(sys.argv) > 4 else None
todo = [c for c in cands if c["url"] not in done and (not tld_filter or c["tld"] == tld_filter)]
random.Random(18).shuffle(todo)
todo = todo[:limit]
lock = threading.Lock()
fo = open(meta_path, "a", encoding="utf-8")
count = [0, 0]


def work(c):
    raw = cc.get(cc.DATA + c["filename"], (c["offset"], c["length"]))
    r = cc.warc_body(raw) if raw else None
    with lock:
        count[0] += 1
        if count[0] % 100 == 0:
            print("fetched", count[0], "stored", count[1], flush=True)
    if not r or not r[1] or len(r[1]) > 1_500_000:
        return
    ctype, body = r
    pid = hashlib.sha1(c["url"].encode()).hexdigest()[:12]
    with open(os.path.join(out, "pages", pid + ".html"), "wb") as f:
        f.write(body)
    rec = {"id": pid, "url": c["url"], "host": c["host"], "tld": c["tld"], "ctype": ctype, "languages": c["languages"],
           "warc": [c["filename"], c["offset"], c["length"]], "size": len(body), "crawl": c["crawl"]}
    with lock:
        fo.write(json.dumps(rec) + "\n")
        fo.flush()
        count[1] += 1


with ThreadPoolExecutor(threads) as ex:
    list(ex.map(work, todo))
print("done", count)
