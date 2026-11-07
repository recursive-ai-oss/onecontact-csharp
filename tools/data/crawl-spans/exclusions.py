#!/usr/bin/env python3
"""Builds the host exclusion list for the crawl-trained span head.

usage: exclusions.py <eval-root> <out-dir> [<extra-dir-or-file> ...]

Every evaluation set in the private evaluation folder was drawn from pages of some host. A model trained on crawled pages must never see
a page of one of those hosts (or of another host of the same site), so this walks the evaluation folder and collects every host named
in a `candidates.jsonl`, `pages.jsonl` or `exclude-hosts.txt` below it, plus any extra folders or files given.

Writes <out-dir>/candidates.jsonl ({"host": ...} lines, the form tools/Eval/extraction/select.py takes as --exclude) and
<out-dir>/sites.txt (the site of every host: its last two labels). Only host names are written, never page text.
"""
import json
import os
import sys

root, out = sys.argv[1], sys.argv[2]
extra = sys.argv[3:]
os.makedirs(out, exist_ok=True)


def site(host):
    parts = host.lower().strip(".").split(".")
    return ".".join(parts[-2:])


hosts = set()


def take(path):
    name = os.path.basename(path)
    try:
        if name.endswith(".jsonl"):
            for line in open(path, encoding="utf-8"):
                line = line.strip()
                if line and '"host"' in line:
                    hosts.add(json.loads(line)["host"].lower())
        elif name == "exclude-hosts.txt":
            for line in open(path, encoding="utf-8"):
                if line.strip():
                    hosts.add(line.strip().lower())
    except (OSError, ValueError, KeyError):
        pass


for base in [root] + extra:
    if os.path.isfile(base):
        take(base)
        continue
    for dirpath, _, files in os.walk(base):
        for f in files:
            if f in ("candidates.jsonl", "pages.jsonl", "exclude-hosts.txt"):
                take(os.path.join(dirpath, f))

with open(os.path.join(out, "candidates.jsonl"), "w", encoding="utf-8") as fo:
    for h in sorted(hosts):
        fo.write(json.dumps({"host": h}) + "\n")
with open(os.path.join(out, "sites.txt"), "w", encoding="utf-8") as fo:
    for s in sorted({site(h) for h in hosts}):
        fo.write(s + "\n")
print("hosts", len(hosts), "sites", len({site(h) for h in hosts}))
