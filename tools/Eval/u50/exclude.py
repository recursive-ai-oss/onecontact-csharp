#!/usr/bin/env python3
"""Lists every host and URL already used by any evaluation set, so a new set can leave them out; also checks a new set against that list.

usage:
  exclude.py build <private root> <out-prefix>      writes <out-prefix>-hosts.txt and <out-prefix>-urls.txt
  exclude.py check <private root> <set dir> [<set dir> ...]
        for each new set directory (holding pages.jsonl and gold-*.jsonl), prints the number of gold items whose page host or URL is in
        the list of the existing stores, and the number shared between the new sets themselves. The answer must be 0 everywhere.

The existing stores are every pages*.jsonl and exclude-hosts*.txt under the private root, outside the folder that holds the new sets.
A leading `www.` is ignored. Only counts are printed.
"""
import glob
import json
import os
import re
import sys


def norm(h):
    return h.lower().removeprefix("www.")


def stores(root, skip=()):
    skip = tuple(os.path.abspath(s) for s in skip)
    hosts, urls = set(), set()
    for p in glob.glob(os.path.join(root, "**", "pages*.jsonl"), recursive=True):
        if any(os.path.abspath(p).startswith(s + os.sep) for s in skip):
            continue
        for l in open(p, encoding="utf-8"):
            if l.strip():
                r = json.loads(l)
                hosts.add(norm(r["host"]))
                urls.add(r["url"])
    for p in glob.glob(os.path.join(root, "**", "exclude-hosts*.txt"), recursive=True):
        if any(os.path.abspath(p).startswith(s + os.sep) for s in skip):
            continue
        hosts |= {norm(l.strip()) for l in open(p, encoding="utf-8") if l.strip()}
    return hosts, urls


def gold_pages(d):
    pages = {}
    for p in glob.glob(os.path.join(d, "**", "pages*.jsonl"), recursive=True):
        for l in open(p, encoding="utf-8"):
            if l.strip():
                r = json.loads(l)
                pages[tuple(r["warc"])] = (norm(r["host"]), r["url"])
    used = set()
    for g in glob.glob(os.path.join(d, "**", "gold-*.jsonl"), recursive=True):
        for l in open(g, encoding="utf-8"):
            if l.strip():
                used.add(tuple(json.loads(l)["warc"]))
    return {w: pages[w] for w in used if w in pages}, len(used)


if sys.argv[1] == "build":
    h, u = stores(sys.argv[2])
    open(sys.argv[3] + "-hosts.txt", "w", encoding="utf-8").write("".join(x + "\n" for x in sorted(h)))
    open(sys.argv[3] + "-urls.txt", "w", encoding="utf-8").write("".join(x + "\n" for x in sorted(u)))
    print("hosts", len(h), "urls", len(u))
else:
    root, dirs = sys.argv[2], sys.argv[3:]
    # Everything under the folder that holds the new sets (their own page stores and exclusion lists) is not an "existing" set.
    hosts, urls = stores(root, skip=sorted({os.path.dirname(os.path.abspath(d)) for d in dirs}))
    seen_h, seen_u = {}, {}
    bad = 0
    for d in dirs:
        g, n = gold_pages(d)
        oh = sum(1 for h, _ in g.values() if h in hosts)
        ou = sum(1 for _, u in g.values() if u in urls)
        unresolved = n - len(g)
        cross = sum(1 for h, u in g.values() if h in seen_h or u in seen_u)
        for h, u in g.values():
            seen_h[h] = d
            seen_u[u] = d
        print(f"{os.path.basename(d)}: pages {n} (unresolved {unresolved}); host overlap with existing sets {oh}; url overlap {ou}; overlap with earlier new sets {cross}")
        bad += oh + ou + unresolved
    print("TOTAL OVERLAP", bad)
