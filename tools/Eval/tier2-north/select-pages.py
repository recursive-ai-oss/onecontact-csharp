#!/usr/bin/env python3
"""Step 1 for country sets drawn by host top-level domain: picks candidate contact/about/team/legal-notice pages from one Common Crawl
index, by random blocks of the sorted URL index.

usage: select-pages.py <crawl-id> <out-dir> <seed> <exclude-hosts-file|-> <tld>:<blocks> [<tld>:<blocks> ...]

Example: select-pages.py CC-MAIN-2026-43 sets/c-north 1 hosts-used.txt dk:60 se:60 is:all

The index (cluster.idx) lists blocks of about 3000 captures in reversed-host order. For every TLD given we draw that many blocks at
random (`all` takes every block), and keep HTML captures with status 200 whose path suggests contact, about, staff, location or
legal-notice content in the country's languages, or the site root (footers). At most two pages per host per block. Hosts listed in the
exclude file (one host per line, as in the earlier evaluation sets) are skipped. Output: candidates-<tlds>.jsonl (run one process per TLD to draw several at once) with the URL, the WARC
filename, offset and length, the TLD and the index's language tags. Nothing about people is read here.
"""
import gzip
import json
import os
import random
import re
import sys

import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "crawl"))
import cc

crawl, out, seed, exclude_file = sys.argv[1], sys.argv[2], int(sys.argv[3]), sys.argv[4]
wanted = {}
for a in sys.argv[5:]:
    t, n = a.split(":")
    wanted[t] = n
os.makedirs(out, exist_ok=True)
base = f"{cc.DATA}cc-index/collections/{crawl}/indexes/"
idx_path = os.path.join(out, "cluster.idx")
if not os.path.exists(idx_path):
    data = cc.get(base + "cluster.idx")
    assert data, "cluster.idx unavailable"
    open(idx_path, "wb").write(data)

excluded = set()
if exclude_file != "-":
    excluded = {l.strip().lower() for l in open(exclude_file, encoding="utf-8") if l.strip()}

blocks = []
for line in open(idx_path, encoding="utf-8"):
    key, rest = line.split("\t", 1)
    fn, off, ln, _ = rest.split("\t")
    blocks.append((key.split(",")[0], key, fn, int(off), int(ln)))

rng = random.Random(seed)
picked = []
for tld, n in wanted.items():
    pool = [b for b in blocks if b[0] == tld]
    take = pool if n == "all" else rng.sample(pool, min(int(n), len(pool)))
    print(tld, "blocks in index", len(pool), "drawn", len(take), flush=True)
    picked += take

# Path words for contact, about, staff, location and legal-notice pages in the languages of the eight countries.
WORDS = ("contact kontakt kontakta kontakte kontakti kontaktai kontaktid kontaktinformation kontaktoplysninger yhteystiedot yhteys ota-yhteytta "
         "about om-os om-oss om-meg om-mig omoss omos meista meist-meile meistä par-mums par-uznemumu apie apie-mus apie-mus "
         "team people staff ansatte medarbejdere medarbetare personal henkilosto henkilöstö tiimi tim darbuotojai darbuotojai komanda "
         "tootajad töötajad kollektiv ledelse ledning johto hallitus vorstand "
         "location locations find-os hitta-hit sijainti asukoht atrasanas buvein adresse adress osoite aadress adrese adresas "
         "impressum juridisk juridiske imprint legal rekvisitter rekvisiter foretaksopplysninger virksomhed foretak yritys yritystiedot "
         "uznemums rekviziti rekvizitai ruzimo ettevote ettevõte firma company cvr organisasjonsnummer "
         "privacy privatliv personvern personvernerklaering integritet tietosuoja privaatsus privatumas privatuma "
         "kapcsolat contacts hvem-er-vi vilka-ar-vi keitä-olemme apie-mus kes-me-oleme").split()
WORDRE = re.compile("|".join(re.escape(w) for w in sorted(set(WORDS), key=len, reverse=True)), re.I)

n_kept = 0
with open(os.path.join(out, "candidates-" + "-".join(wanted) + ("" if seed == 1 else f"-s{seed}") + ".jsonl"), "w", encoding="utf-8") as fo:
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
            tld = host.rsplit(".", 1)[-1]
            if tld != b[0] or host in excluded or host.removeprefix("www.") in excluded:
                continue
            if not (WORDRE.search(path) or path in ("/", "/index.html", "/index.php", "/index.htm")):
                continue
            if seen_hosts.get(host, 0) >= 2:
                continue
            seen_hosts[host] = seen_hosts.get(host, 0) + 1
            fo.write(json.dumps({"url": url, "host": host, "tld": tld, "filename": j["filename"], "offset": int(j["offset"]),
                                 "length": int(j["length"]), "languages": j.get("languages", ""), "crawl": crawl}) + "\n")
            n_kept += 1
print("blocks", len(picked), "candidates", n_kept)
