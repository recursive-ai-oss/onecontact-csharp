#!/usr/bin/env python3
"""Prints, for each wrongly labelled person item, only the word shapes with the gold and predicted label sequences (no letters of the text).

usage: name-shapes.py --gold gold.jsonl --pred pred.jsonl

A word's shape is U (upper case), C (capitalized), l (lower case), N (digits), d (ends with a dot), P (has punctuation inside) joined, never the characters themselves.
It lets a rule author see which kind of string goes wrong on a private set without reading any item.
"""
import argparse
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import score  # noqa: E402


def shape(w):
    letters = re.sub(r"[\W\d_]", "", w)
    if not letters:
        s = "N" if re.search(r"\d", w) else "P"
    elif letters.isupper() and len(letters) > 1:
        s = "U"
    elif letters[0].isupper():
        s = "C"
    else:
        s = "l"
    if w.endswith(","):
        s += ","
    if w.endswith("."):
        s += "."
    return s


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--gold", required=True)
    ap.add_argument("--pred", required=True)
    args = ap.parse_args()
    gold, pred = score.load(args.gold), score.load(args.pred)
    for i, g in sorted(gold.items()):
        if g["class"] not in ("person", "multiple"):
            continue
        words = g["text"].split()
        gl = g["labels"].split()
        d = pred.get(i)
        pl = score.assign(g["text"], d.get("parts", {})) if d else ["O"] * len(words)
        if gl != pl:
            print(" ".join(shape(w) for w in words), "| gold", "".join(gl), "| ours", "".join(pl), "|", d.get("class") if d else None)


if __name__ == "__main__":
    main()
