#!/usr/bin/env python3
"""Why a no-hint address was refused or read with the wrong country, as counts only.

usage: diag-outcomes.py --diag diag.jsonl --pred predictions.jsonl

`diag.jsonl` is written by `eval-rules diag`: for each item the countries that read it (given as the hint), grouped by identical fields, and the labels in
which the groups differ. The table counts refused items by how many countries read them, how many different readings there were, whether the right country
was among them, and which labels differ; no item text or id is printed.
"""
import argparse
import json
import os
import sys
from collections import Counter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import score  # noqa: E402


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--diag", required=True)
    ap.add_argument("--pred", required=True)
    args = ap.parse_args()
    diag = score.load(args.diag)
    pred = score.load(args.pred)
    refused = Counter()
    splits = Counter()
    wrong = Counter()
    for i, d in diag.items():
        p = pred.get(i, {})
        gold = str(d["gold"]).upper()
        in_reads = gold in d["reads"]
        key = (len(d["reads"]) if len(d["reads"]) < 4 else "4+", len(d["groups"]) if len(d["groups"]) < 3 else "3+", "gold-reads" if in_reads else "gold-does-not-read", " ".join(sorted(d["differ"])) or "-")
        if not p.get("fields"):
            refused[key] += 1
            if len(d["groups"]) > 1:
                splits[" | ".join(",".join(sorted(g)) for g in sorted(d["groups"], key=lambda g: sorted(g)))] += 1
        elif p.get("decided_country") and p["decided_country"].upper() != gold:
            wrong[(p["decided_country"].upper() + "<-" + gold,) + key] += 1
    print("-- refused: (countries that read, distinct readings, whether the right country reads, labels that differ) count")
    for k, n in refused.most_common(14):
        print(f"{n:4}  {k}")
    print("-- refused with several readings: which countries read alike")
    for k, n in splits.most_common(8):
        print(f"{n:4}  {k}")
    print("-- wrong country: (decided<-gold, ...) count")
    for k, n in wrong.most_common(8):
        print(f"{n:4}  {k}")


main()
