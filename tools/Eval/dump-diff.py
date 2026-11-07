#!/usr/bin/env python3
"""Summarizes the difference between two `dump-address` outputs (tools/Eval/differential.sh): per country and mode, how many cases changed and how.

usage: dump-diff.py before/address.txt after/address.txt [--show CC]

A record starts at a line that begins with "<CC> none " or "<CC> hint "; other lines continue the previous record (canonical forms hold line breaks).
Classes: refused->read, read->refused, country (the decided country changed), fields (same country, other fields), key (only the canonical, display or key text changed).
The last line counts, per country of the register, the no-hint records that were read as a country other than the register's (ZZ, the undetermined country, is not one).
"""
import re
import sys

START = re.compile(r"^([A-Z]{2}) (none|hint) ")
REFUSED = {"InvalidData", "NeedMoreData", "DestinationTooSmall"}
FIELD = re.compile(r"\[[^\]|]+\|[^\]|]*\|[^\]|]*\|\d+\+\d+")


def load(path):
    recs, cur = {}, None
    for line in open(path, encoding="utf-8"):
        line = line.rstrip("\n")
        if START.match(line):
            cur = [line]
            recs.setdefault(line.split(" => ", 1)[0], cur)
        elif cur is not None:
            cur.append(line)
    return {k: "\n".join(v) for k, v in recs.items()}


def outcome(rec):
    body = rec.split(" => ", 1)[1] if " => " in rec else rec
    return body.split(" ", 1)[0]


a, b = load(sys.argv[1]), load(sys.argv[2])
show = sys.argv[sys.argv.index("--show") + 1] if "--show" in sys.argv else None
stats, wrong = {}, {}
for head in sorted(set(a) | set(b)):
    ra, rb = a.get(head), b.get(head)
    cc, mode = head.split(" ")[0], head.split(" ")[1]
    for tag, r in (("before", ra), ("after", rb)):
        if r is not None and mode == "none":
            o = outcome(r)
            if len(o) == 2 and o.isalpha() and o.isupper() and o not in ("ZZ", cc):
                wrong[(cc, tag)] = wrong.get((cc, tag), 0) + 1
    if ra == rb:
        continue
    if ra is None or rb is None:
        kind = "missing"
    else:
        oa, ob = outcome(ra), outcome(rb)
        if oa in REFUSED and ob not in REFUSED:
            kind = "refused->read"
        elif oa not in REFUSED and ob in REFUSED:
            kind = "read->refused"
        elif oa != ob:
            kind = f"country {oa}->{ob}"
        else:
            kind = "fields" if FIELD.findall(ra) != FIELD.findall(rb) else "key"
    stats.setdefault((cc, mode, kind), []).append(head)
for (cc, mode, kind), heads in sorted(stats.items()):
    print(f"{cc} {mode:4} {kind}: {len(heads)}")
    if show == cc:
        for h in heads[:15]:
            print("   ", h[:160])
print("wrong-country reads, no-hint records:", {f"{k[0]} {k[1]}": v for k, v in sorted(wrong.items())})
