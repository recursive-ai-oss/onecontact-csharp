#!/usr/bin/env python3
"""Step 1 of the extraction evaluation sets: draws candidate captures from one Common Crawl index.

usage: select.py <crawl-id> <out-dir> <seed> web|ugc <blocks-us> <blocks-ca> [--exclude <dir> ...]

Like the address and name selection (tools/Eval/crawl/select-pages.py), it reads the crawl's cluster.idx and draws random index
blocks with a fixed seed, then keeps HTML captures with status 200 from those blocks, at most two per host per block. Hosts that
appear in any `candidates.jsonl` of an `--exclude` directory are skipped, so a page or host used for another evaluation set can
never be drawn again.

web: contact, about, team, staff and location paths, the site root, and (about one in six) pages with any other path, so that
     pages with no contact data are in the draw.
ugc: forum, thread, topic, classified, comment, review and blog-post paths, where people write free text about themselves.
"""
import json
import os
import random
import re
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "crawl"))
import cc  # noqa: E402

args = sys.argv[1:]
exclude_dirs = []
if "--exclude" in args:
    i = args.index("--exclude")
    exclude_dirs = args[i + 1:]
    args = args[:i]
crawl, out, seed, kind, n_us, n_ca = args[0], args[1], int(args[2]), args[3], int(args[4]), int(args[5])
os.makedirs(out, exist_ok=True)

excluded = set()
for d in exclude_dirs:
    p = os.path.join(d, "candidates.jsonl")
    if os.path.exists(p):
        for line in open(p, encoding="utf-8"):
            excluded.add(json.loads(line)["host"])

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
tld_of = lambda b: b[0].split(",")[0]  # noqa: E731
rng = random.Random(seed)
us_blocks = [b for b in blocks if tld_of(b) in US_TLDS]
ca_blocks = [b for b in blocks if tld_of(b) == "ca"]
picked = rng.sample(us_blocks, min(n_us, len(us_blocks))) + rng.sample(ca_blocks, min(n_ca, len(ca_blocks)))

WEB = re.compile(r"/(contact|contact-us|contactus|about|about-us|aboutus|team|our-team|staff|people|leadership|attorneys|lawyers|doctors|providers|"
                 r"locations?|find-us|visit|directions|hours|nous-joindre|contactez-nous|a-propos|notre-equipe|equipe|coordonnees)(/|\.|$|-)", re.I)
UGC = re.compile(r"/(forum|forums|thread|threads|topic|topics|viewtopic|showthread|classifieds?|ads?|listing|listings|comments?|reviews?|discussion|"
                 r"post|posts|board|community|questions?|guestbook|wanted|for-sale|rentals?)(/|\.|$|-|\?)|/\d{4}/\d{2}/", re.I)

kept = 0
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
        per_host = {}
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
            m = re.match(r"https?://([^/]+)(/[^?#]*)?(\?[^#]*)?", j["url"])
            if not m:
                continue
            host, path = m.group(1).lower(), (m.group(2) or "/") + (m.group(3) or "")
            if host in excluded:
                continue
            if kind == "web":
                ok = bool(WEB.search(path)) or path in ("/", "/index.html", "/index.php") or rng.random() < 0.02
            else:
                ok = bool(UGC.search(path))
            if not ok or per_host.get(host, 0) >= 2:
                continue
            per_host[host] = per_host.get(host, 0) + 1
            fo.write(json.dumps({"url": j["url"], "host": host, "tld": host.rsplit(".", 1)[-1], "filename": j["filename"],
                                 "offset": int(j["offset"]), "length": int(j["length"]), "languages": j.get("languages", ""),
                                 "crawl": crawl}) + "\n")
            kept += 1
print("blocks", len(picked), "candidates", kept, "excluded hosts", len(excluded))
