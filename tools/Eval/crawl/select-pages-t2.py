#!/usr/bin/env python3
"""Step 1 (Tier 2): picks candidate contact/legal-notice/about/team pages of one TLD group from a Common Crawl index, by random blocks.

usage: select-pages-t2.py <crawl-id> <out-dir> <seed> <tld>=<blocks> [<tld>=<blocks> ...]

Blocks are drawn from the sorted URL index (cluster.idx) among those of the given host TLD; a count of 0 means every block. From each
block the HTML captures with status 200 whose path suggests contact, legal-notice, about, team or location content, or the site root
(footers), are kept, at most two per host per block. Hosts listed (one per line) in the file named by EXCLUDE_HOSTS are skipped.
Output: <out-dir>/candidates.jsonl, appended with a `country` field. Nothing about people is read here.
"""
import gzip
import json
import os
import random
import re
import sys

import cc
import tier2

crawl, out, seed = sys.argv[1], sys.argv[2], int(sys.argv[3])
wanted = [(a.split("=")[0], int(a.split("=")[1])) for a in sys.argv[4:]]
os.makedirs(out, exist_ok=True)
base = f"{cc.DATA}cc-index/collections/{crawl}/indexes/"
idx_path = os.path.join(out, "cluster.idx")
if not os.path.exists(idx_path):
    data = cc.get(base + "cluster.idx")
    assert data, "cluster.idx unavailable"
    open(idx_path, "wb").write(data)
exclude = set()
if os.environ.get("EXCLUDE_HOSTS"):
    exclude = {l.strip().lower() for l in open(os.environ["EXCLUDE_HOSTS"], encoding="utf-8") if l.strip()}

blocks = []
for line in open(idx_path, encoding="utf-8"):
    key, rest = line.split("\t", 1)
    fn, off, ln, _ = rest.split("\t")
    blocks.append((key.split(",")[0], key, fn, int(off), int(ln)))

rng = random.Random(seed)
picked = []
for tld, n in wanted:
    pool = [b for b in blocks if b[0] == tld]
    picked += pool if n == 0 or n >= len(pool) else rng.sample(pool, n)
    print(tld, "blocks available", len(pool), "picked", len(pool) if n == 0 or n >= len(pool) else n)

n_kept = 0
path = os.path.join(out, "candidates.jsonl")
done = set()
if os.path.exists(path):
    for l in open(path, encoding="utf-8"):
        done.add(json.loads(l)["url"])
with open(path, "a", encoding="utf-8") as fo:
    for b in picked:
        raw = cc.get(base + b[2], (b[3], b[4]))
        if raw is None:
            continue
        try:
            txt = gzip.decompress(raw).decode("utf-8", "replace")
        except Exception:
            continue
        seen_hosts = {}
        for line in txt.splitlines():
            parts = line.split(" ", 2)
            if len(parts) < 3:
                continue
            try:
                j = json.loads(parts[2])
            except Exception:
                continue
            if j.get("status") != "200" or "html" not in j.get("mime-detected", j.get("mime", "")):
                continue
            url = j["url"]
            m = re.match(r"https?://([^/]+)(/[^?#]*)?", url)
            if not m:
                continue
            host, p = m.group(1).lower(), m.group(2) or "/"
            info = tier2.host_info(host)
            if not info or host in exclude or host.removeprefix("www.") in exclude or url in done:
                continue
            if not (tier2.PATH.search(p) or p in tier2.ROOTS):
                continue
            if seen_hosts.get(host, 0) >= 2:
                continue
            seen_hosts[host] = seen_hosts.get(host, 0) + 1
            fo.write(json.dumps({"url": url, "host": host, "tld": host.rsplit(".", 1)[-1], "country": info[0], "territory": info[1],
                                 "filename": j["filename"], "offset": int(j["offset"]), "length": int(j["length"]),
                                 "languages": j.get("languages", ""), "crawl": crawl}) + "\n")
            n_kept += 1
print("blocks", len(picked), "candidates", n_kept)
