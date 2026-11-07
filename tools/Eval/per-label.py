#!/usr/bin/env python3
"""Per-label precision, recall and F1 of address predictions, aggregates only (no item text is printed).

usage: per-label.py --gold gold.jsonl --pred NAME=pred.jsonl [--pred ...]

Uses the scorer's coarse labels and its text normalization (score.py), so the numbers add up to the scorer's field F1: a value is right when its normalized
text equals the gold value of the same label.
"""
import argparse
import os
import sys
from collections import defaultdict

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import score  # noqa: E402


def labels(item):
    out = {}
    src = item.get("fields", {})
    for k in score.COARSE:
        v = src.get(k)
        if v:
            n = score.norm(v)
            if k == "postcode":
                import re
                n = re.sub(r"^[a-z]{1,2}(?=\d{4,5}$)", "", n)
            if n:
                out[k] = n
    return out


def shape(text):
    import re
    out = re.sub(r"[0-9]+", "9", str(text))
    return re.sub(r"[^\W\d_]+", "a", out)


def shapes(gold, pred, label):
    hist = defaultdict(int)
    detail = defaultdict(int)
    for gid, g in gold.items():
        gv = g.get("fields", {}).get(label)
        if not gv:
            continue
        pv = pred.get(gid, {}).get("fields", {}).get(label)
        ng, npv = score.norm(gv), score.norm(pv) if pv else ""
        if ng == npv:
            kind = "right"
        elif not npv:
            kind = "missing"
        elif npv in ng:
            kind = "inside-gold"
        elif ng in npv:
            kind = "around-gold"
        else:
            kind = "elsewhere"
        hist[(shape(gv), kind)] += 1
        if kind in ("around-gold", "elsewhere", "inside-gold"):
            other = [k for k, v in g.get("fields", {}).items() if k != label and v and score.norm(v) and (score.norm(v) in npv or npv in score.norm(v))]
            words_g, words_p = score.norm(gv), npv
            extra = ("extra-before" if words_p.endswith(words_g) else "extra-after" if words_p.startswith(words_g) else "extra-inside") if kind == "around-gold" else kind
            if extra == "extra-before" and pv:
                # The shape of the surplus words (digits 9, letters a): the words of the prediction that come before the gold value in the prediction's own text.
                cut = score.norm(pv).rfind(words_g)
                n_extra = len(pv.split()) - len(gv.split())
                detail[(kind, extra + " %d word(s) " % n_extra + " ".join(shape(w) for w in pv.split()[:max(n_extra, 0)]), ",".join(sorted(other)) or "-")] += 1
            else:
                detail[(kind, extra, ",".join(sorted(other)) or "-")] += 1
    print(f"-- shapes of {label} (gold shape, what the first prediction did)")
    for (sh, kind), n in sorted(hist.items(), key=lambda kv: -kv[1])[:14]:
        print(f"{n:4}  {sh:14} {kind}")
    print("-- misses overlapping another gold label (kind, where, label names)")
    for (kind, extra, other), n in sorted(detail.items(), key=lambda kv: -kv[1])[:10]:
        print(f"{n:4}  {kind:12} {extra:13} {other}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--gold", required=True)
    ap.add_argument("--pred", action="append", default=[])
    ap.add_argument("--shapes", help="label: also print, for the first prediction, how its misses of that label are distributed by the shape of the gold value (digits 9, letters a, runs collapsed) and by what the prediction did (missing, inside the gold value, around it, elsewhere)")
    args = ap.parse_args()
    gold = score.load(args.gold)
    print(f"items {len(gold)}")
    for spec in args.pred:
        name, path = spec.split("=", 1)
        pred = score.load(path)
        tp = defaultdict(int)
        npred = defaultdict(int)
        ngold = defaultdict(int)
        for gid, g in gold.items():
            gl = labels(g)
            pl = labels(pred[gid]) if gid in pred else {}
            for k, v in gl.items():
                ngold[k] += 1
            for k, v in pl.items():
                npred[k] += 1
                if gl.get(k) == v:
                    tp[k] += 1
        if args.shapes and spec == args.pred[0]:
            shapes(gold, pred, args.shapes)
        print(f"-- {name}")
        print(f"{'label':14} {'gold':>5} {'pred':>5} {'right':>5} {'P':>6} {'R':>6} {'F1':>6}")
        for k in score.COARSE:
            if ngold[k] == 0 and npred[k] == 0:
                continue
            p = tp[k] / npred[k] if npred[k] else 0.0
            r = tp[k] / ngold[k] if ngold[k] else 0.0
            f = 2 * p * r / (p + r) if p + r else 0.0
            print(f"{k:14} {ngold[k]:5} {npred[k]:5} {tp[k]:5} {100*p:6.1f} {100*r:6.1f} {100*f:6.1f}")


main()
