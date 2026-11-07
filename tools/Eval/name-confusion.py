#!/usr/bin/env python3
"""Counts gold-label -> predicted-label word pairs of person and multiple items, aggregates only (no item text is printed).

usage: name-confusion.py --gold gold.jsonl --pred NAME=pred.jsonl [--pred ...]

Uses the scorer's word assignment (score.py), so the diagonal adds up to its person token accuracy. Also prints, per word count of the item, how many items
are fully right, which shows whether the misses sit in short or long names.
"""
import argparse
import os
import sys
from collections import Counter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import score  # noqa: E402


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--gold", required=True)
    ap.add_argument("--pred", action="append", default=[])
    args = ap.parse_args()
    gold = score.load(args.gold)
    for p in args.pred:
        name, path = p.split("=", 1)
        pred = score.load(path)
        pairs, by_len, wrong_class = Counter(), Counter(), Counter()
        for i, g in sorted(gold.items()):
            if g["class"] not in ("person", "multiple"):
                continue
            words = g["text"].split()
            gl = g["labels"].split()
            d = pred.get(i)
            pl = score.assign(g["text"], d.get("parts", {})) if d else ["O"] * len(words)
            ok = True
            for a, b in zip(gl, pl):
                pairs[(a, b)] += 1
                ok = ok and a == b
            by_len[(len(words), ok)] += 1
            if d and d.get("class") != g["class"]:
                wrong_class[(g["class"], d.get("class"))] += 1
        print(f"== {name}")
        total = sum(pairs.values())
        right = sum(v for (a, b), v in pairs.items() if a == b)
        print(f"words {total} right {right} ({100.0 * right / max(total, 1):.2f})")
        for (a, b), v in sorted(pairs.items(), key=lambda kv: -kv[1]):
            if a != b:
                print(f"  gold {a} -> {b}: {v}")
        for n in sorted({k[0] for k in by_len}):
            print(f"  items with {n} words: right {by_len[(n, True)]} wrong {by_len[(n, False)]}")
        for (a, b), v in sorted(wrong_class.items(), key=lambda kv: -kv[1]):
            print(f"  class {a} -> {b}: {v}")


if __name__ == "__main__":
    main()
