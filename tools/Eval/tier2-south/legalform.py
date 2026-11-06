#!/usr/bin/env python3
"""Scores legal-form detection on the company items of a name evaluation set, with paired bootstrap 95% intervals.

usage: legalform.py --gold gold-name-CC.jsonl --pred NAME=pred.jsonl [--pred ...] [--reference NAME] [--resamples 1000] [--seed 20261105]

Gold: a company item has a legal form when one of its word labels is L. A system detects one when it returns a non-empty L part
(cleanco: its documented `basename` changed the name, written as has_form by run-eval-names.py). The score is the share of company
items whose has-a-legal-form answer is right, and, over the items that have one, the share whose L words equal the gold L words
(folded the way score.py folds). Only numbers are printed.
"""
import argparse
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import score  # noqa: E402


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--gold", required=True)
    ap.add_argument("--pred", action="append", default=[])
    ap.add_argument("--reference")
    ap.add_argument("--resamples", type=int, default=1000)
    ap.add_argument("--seed", type=int, default=20261105)
    args = ap.parse_args()
    gold = {i: g for i, g in score.load(args.gold).items() if g["class"] == "company"}
    ids = sorted(gold)
    preds = {p.split("=", 1)[0]: score.load(p.split("=", 1)[1]) for p in args.pred}
    detect, exact = {}, {}
    for n, d in preds.items():
        det, ex = [], []
        for i in ids:
            g = gold[i]
            words = g["text"].split()
            gl = [w for w, l in zip(words, g["labels"].split()) if l == "L"]
            o = d.get(i, {})
            pl = [w for v in o.get("parts", {}).get("L", []) for w in str(v).split()]
            has = bool(o["has_form"]) if "has_form" in o else bool(pl)
            det.append((1 if has == bool(gl) else 0, 1))
            ex.append((1 if (gl and [score.norm(x) for x in gl] == [score.norm(x) for x in pl]) else 0, 1 if gl else 0))
        detect[n], exact[n] = det, ex
    out = []
    ns = argparse.Namespace(resamples=args.resamples, seed=args.seed, reference=args.reference)
    score.report(score.ratio_stat, "legal-form detection (company items)", detect, len(ids), ns, out)
    score.report(score.ratio_stat, "legal-form words equal gold (items with a form)", exact, len(ids), ns, out)
    # The second metric's denominator is the items with a form, so resample over all items with that ratio statistic.
    for e in out:
        extra = ""
        k = [x for x in e if x.startswith("vs_")]
        if k:
            extra = f"  {k[0]} {e[k[0]]['diff']:+.2f} [{e[k[0]]['ci95'][0]:+.2f}, {e[k[0]]['ci95'][1]:+.2f}]"
        print(f"{e['metric']:<52} {e['system']:<16} n={e['n_items']:<4} {e['score']:6.2f} [{e['ci95'][0]:6.2f}, {e['ci95'][1]:6.2f}]{extra}")
    print("gold companies with a legal form:", sum(1 for i in ids if "L" in gold[i]["labels"].split()), "of", len(ids))


if __name__ == "__main__":
    main()
