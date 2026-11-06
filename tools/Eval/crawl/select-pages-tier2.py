#!/usr/bin/env python3
"""Step 1 (Tier 2 group B): picks candidate contact/about/legal-notice pages from one Common Crawl index, by random blocks of the
sorted URL index, per country TLD.

usage: select-pages-tier2.py <crawl-id> <out-dir> <seed> <cluster.idx or -> <CC:blocks>[,<CC:blocks>...] [exclude-hosts-file]

`blocks` is a number of random index blocks, or `all` for every block of that TLD (the small states have few captures). The index
(cluster.idx) lists blocks of ~3000 captures in reversed-host order; the TLD is the first label of a block's key. From each block we
keep HTML captures with status 200 whose path suggests contact, about, team, location or legal-notice content (Impressum, mentions
légales, aviso legal, note legali, and the like), or the site root (footers); for the small TLDs (blocks `all`) any HTML page is
kept, at most three per host per block. At most two pages per host per block otherwise. Hosts listed in the exclude file (one per
line: hosts used by earlier evaluation sets) are skipped. Output: candidates.jsonl (url, host, tld, WARC reference, languages).
Nothing about people is read here.
"""
import gzip
import json
import os
import random
import re
import sys

import cc
import tier2

crawl, out, seed, idx_arg, plan = sys.argv[1], sys.argv[2], int(sys.argv[3]), sys.argv[4], sys.argv[5]
excluded = set()
if len(sys.argv) > 6:
    excluded = {l.strip().lower() for l in open(sys.argv[6], encoding="utf-8") if l.strip()}
os.makedirs(out, exist_ok=True)
base = f"{cc.DATA}cc-index/collections/{crawl}/indexes/"
idx_path = os.path.join(out, "cluster.idx")
if idx_arg != "-" and not os.path.exists(idx_path):
    os.symlink(os.path.abspath(idx_arg), idx_path)
if not os.path.exists(idx_path):
    data = cc.get(base + "cluster.idx")
    assert data, "cluster.idx unavailable"
    open(idx_path, "wb").write(data)

blocks = []
for line in open(idx_path, encoding="utf-8"):
    key, rest = line.split("\t", 1)
    fn, off, ln, _ = rest.split("\t")
    blocks.append((key.split(")")[0].split(",")[0], key, fn, int(off), int(ln)))

PATH = re.compile(
    r"/(contact|contact-us|contactus|contacts|about|about-us|aboutus|team|our-team|staff|people|leadership|solicitors|lawyers|locations?|find-us|visit|directions|"
    r"impressum|legal|legal-notice|legal-notices|terms|privacy|privacy-policy|"
    r"contacto|contactenos|contactanos|contactar|aviso-legal|aviso_legal|quienes-somos|quien-somos|sobre-nosotros|nosotros|equipo|donde-estamos|ubicacion|politica-de-privacidad|"
    r"contatti|contattaci|contatto|chi-siamo|note-legali|privacy|dove-siamo|sedi|sede|azienda|il-nostro-team|"
    r"contactos|contacte|fale-conosco|sobre-nos|quem-somos|empresa|aviso-legal|informacoes-legais|onde-estamos|equipa|"
    r"contact-details|get-in-touch|find-us|our-offices|offices|branches|stockists|where-to-find-us|clinics|surgery)(/|\.|$|-)", re.I)

rng = random.Random(seed)
n_kept = 0
with open(os.path.join(out, "candidates.jsonl"), "a", encoding="utf-8") as fo:
    for item in plan.split(","):
        country, count = item.split(":")
        tlds = tier2.TLDS[country]
        pool = [b for b in blocks if b[0] in tlds]
        small = count == "all"
        picked = pool if small else rng.sample(pool, min(int(count), len(pool)))
        kept_c = 0
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
                host, path = m.group(1).lower(), m.group(2) or "/"
                if host in excluded or host.removeprefix("www.") in excluded:
                    continue
                if not small and not (PATH.search(path) or path in ("/", "/index.html", "/index.php", "/index.htm")):
                    continue
                if seen_hosts.get(host, 0) >= (3 if small else 2):
                    continue
                seen_hosts[host] = seen_hosts.get(host, 0) + 1
                tld = host.rsplit(".", 1)[-1]
                fo.write(json.dumps({"url": url, "host": host, "tld": tld, "country": country, "filename": j["filename"], "offset": int(j["offset"]),
                                     "length": int(j["length"]), "languages": j.get("languages", ""), "crawl": crawl}) + "\n")
                n_kept += 1
                kept_c += 1
        print(country, "blocks", len(picked), "of", len(pool), "candidates", kept_c, flush=True)
print("candidates", n_kept)
