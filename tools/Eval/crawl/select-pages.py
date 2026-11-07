#!/usr/bin/env python3
"""Step 1: picks candidate contact/about/staff/footer pages from one Common Crawl index, by random blocks of the sorted URL index.

usage: select-pages.py <crawl-id> <out-dir> <seed> <blocks-us> <blocks-ca> [balanced]

With EXCLUDE_HOSTS=<file> (one host per line, see ../u50/exclude.py) hosts listed there are skipped, so a host used by another evaluation
set never appears in a new one. A leading `www.` is ignored when comparing.

With `balanced` the United States blocks are drawn evenly across the host TLDs instead of in proportion to their size (a top-up draw:
.org, .edu, .gov and .us hosts are far more often American than the very large .com share).

The index (cluster.idx) lists blocks of ~3000 captures in reversed-host order. For United States candidates we draw blocks from
the .com/.org/.net/.us/.edu/.gov/.biz hosts, for Canadian candidates from .ca hosts, plus French-language captures in .ca.
From each block we keep HTML captures with status 200 whose path suggests contact/about/team/location content, or the site root
(footers). Output: candidates.jsonl with url, WARC filename/offset/length, languages. Nothing about people is read here.
"""
import json
import os
import random
import re
import sys

import cc

crawl, out, seed, n_us, n_ca = sys.argv[1], sys.argv[2], int(sys.argv[3]), int(sys.argv[4]), int(sys.argv[5])
os.makedirs(out, exist_ok=True)
base = f"{cc.DATA}cc-index/collections/{crawl}/indexes/"
idx_path = os.path.join(out, "cluster.idx")
if not os.path.exists(idx_path):
    data = cc.get(base + "cluster.idx")
    assert data, "cluster.idx unavailable"
    open(idx_path, "wb").write(data)

blocks = []
for line in open(idx_path, encoding="utf-8"):
    key, rest = line.split("\t", 1)
    fn, off, ln, _ = rest.split("\t")
    blocks.append((key.split(")")[0], key, fn, int(off), int(ln)))

US_TLDS = ("com", "org", "net", "us", "edu", "gov", "biz", "info")
def host_tld(b):
    return b[0].split(",")[0]

EXCLUDE = set()
if os.environ.get("EXCLUDE_HOSTS"):
    EXCLUDE = {l.strip().lower().removeprefix("www.") for l in open(os.environ["EXCLUDE_HOSTS"], encoding="utf-8") if l.strip()}

rng = random.Random(seed)
us_blocks = [b for b in blocks if host_tld(b) in US_TLDS]
ca_blocks = [b for b in blocks if host_tld(b) == "ca"]
if len(sys.argv) > 6 and sys.argv[6] == "balanced":
    groups = {t: [b for b in us_blocks if host_tld(b) == t] for t in US_TLDS}
    us_pick = []
    while len(us_pick) < n_us:
        for t in US_TLDS:
            if groups[t] and len(us_pick) < n_us:
                us_pick.append(groups[t].pop(rng.randrange(len(groups[t]))))
else:
    us_pick = rng.sample(us_blocks, min(n_us, len(us_blocks)))
picked = us_pick + rng.sample(ca_blocks, min(n_ca, len(ca_blocks)))

PATH = re.compile(r"/(contact|contact-us|contactus|about|about-us|aboutus|team|our-team|staff|people|leadership|attorneys|lawyers|doctors|providers|"
                  r"locations?|find-us|visit|directions|hours|nous-joindre|contactez-nous|a-propos|notre-equipe|equipe|coordonnees)(/|\.|$|-)", re.I)
n_kept = 0
with open(os.path.join(out, "candidates.jsonl"), "w", encoding="utf-8") as fo:
    for b in picked:
        raw = cc.get(base + b[2], (b[3], b[4]))
        if raw is None:
            continue
        import gzip
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
            host, path = m.group(1).lower(), m.group(2) or "/"
            if host.removeprefix("www.") in EXCLUDE:
                continue
            if not (PATH.search(path) or path in ("/", "/index.html", "/index.php")):
                continue
            # at most two pages per host per block, so one large site doesn't dominate.
            if seen_hosts.get(host, 0) >= 2:
                continue
            seen_hosts[host] = seen_hosts.get(host, 0) + 1
            tld = host.rsplit(".", 1)[-1]
            fo.write(json.dumps({"url": url, "host": host, "tld": tld, "filename": j["filename"], "offset": int(j["offset"]), "length": int(j["length"]),
                                 "languages": j.get("languages", ""), "crawl": crawl}) + "\n")
            n_kept += 1
print("blocks", len(picked), "candidates", n_kept)
