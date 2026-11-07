#!/usr/bin/env python3
"""Per-outcome aggregates of address predictions: how many items were refused, read with no country, read with the right country, read with a wrong one,
and the mean item F1 of each group. Counts only: no item text, id or value is printed.

usage: outcomes.py --gold gold.jsonl --pred NAME=pred.jsonl [--pred ...]

A prediction counts as refused when it has no field. `decided_country` is written by tools/Eval/Rules (empty when the parser settled on no country);
predictions of other systems (no such key) are only split into refused and read.
"""
import argparse
import os
import re
import sys
from collections import defaultdict

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import score  # noqa: E402


def labels(item):
    out = {}
    for k in score.COARSE:
        v = item.get("fields", {}).get(k)
        if v:
            n = score.norm(v)
            if k == "postcode":
                n = re.sub(r"^[a-z]{1,2}(?=\d{4,5}$)", "", n)
            if n:
                out[k] = n
    return out


def item_f1(g, p):
    gl, pl = labels(g), labels(p)
    tp = sum(1 for k, v in pl.items() if gl.get(k) == v)
    if not gl or not pl or not tp:
        return 0.0
    pr, rc = tp / len(pl), tp / len(gl)
    return 2 * pr * rc / (pr + rc)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--gold", required=True)
    ap.add_argument("--pred", action="append", default=[])
    args = ap.parse_args()
    gold = score.load(args.gold)
    print(f"items {len(gold)}")
    for spec in args.pred:
        name, path = spec.split("=", 1)
        pred = score.load(path)
        groups = defaultdict(list)
        wrong = defaultdict(int)
        for gid, g in gold.items():
            p = pred.get(gid, {})
            if not p.get("fields"):
                groups["refused"].append(0.0)
                continue
            f1 = item_f1(g, p)
            dc = p.get("decided_country")
            if dc is None:
                groups["read"].append(f1)
            elif dc == "":
                groups["read-no-country"].append(f1)
            elif dc.upper() == str(g.get("country", "")).upper():
                groups["read-right-country"].append(f1)
            else:
                groups["read-wrong-country"].append(f1)
                wrong[dc.upper() + "<-" + str(g.get("country", "")).upper()] += 1
        print(f"-- {name}")
        for k in ("refused", "read", "read-no-country", "read-right-country", "read-wrong-country"):
            v = groups.get(k)
            if v:
                print(f"{k:20} {len(v):4}  mean item F1 {100 * sum(v) / len(v):6.1f}")
        if wrong:
            print("  wrong country (decided<-gold): " + ", ".join(f"{k} {n}" for k, n in sorted(wrong.items(), key=lambda kv: -kv[1])))


main()
