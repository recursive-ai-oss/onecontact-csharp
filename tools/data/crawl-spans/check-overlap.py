#!/usr/bin/env python3
"""Checks that a set of crawled pages shares no host and no site with the evaluation sets, and drops the candidates that do.

usage: check-overlap.py filter <exclusions-dir> <candidates.jsonl> <out.jsonl>   writes the candidates whose site is not excluded
       check-overlap.py check  <exclusions-dir> <pages.jsonl>                    prints the number of pages whose host or site is excluded

<exclusions-dir> is the output of exclusions.py. The check prints counts only.
"""
import json
import sys

mode, excl = sys.argv[1], sys.argv[2]
hosts = {json.loads(l)["host"] for l in open(f"{excl}/candidates.jsonl", encoding="utf-8")}
sites = {l.strip() for l in open(f"{excl}/sites.txt", encoding="utf-8") if l.strip()}


def site(host):
    return ".".join(host.lower().strip(".").split(".")[-2:])


def excluded(host):
    return host.lower() in hosts or site(host) in sites


if mode == "filter":
    kept = dropped = 0
    with open(sys.argv[4], "w", encoding="utf-8") as fo:
        for line in open(sys.argv[3], encoding="utf-8"):
            if excluded(json.loads(line)["host"]):
                dropped += 1
            else:
                fo.write(line)
                kept += 1
    print("kept", kept, "dropped", dropped)
else:
    pages = [json.loads(l) for l in open(sys.argv[3], encoding="utf-8") if l.strip()]
    bad = sum(1 for p in pages if excluded(p["host"]))
    print("pages", len(pages), "excluded-host-or-site overlaps", bad)
    sys.exit(1 if bad else 0)
