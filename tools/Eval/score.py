#!/usr/bin/env python3
"""Scores address and name predictions against a gold set and prints aggregates with paired bootstrap 95% confidence intervals.

usage: score.py address|name --gold gold.jsonl --pred NAME=pred.jsonl [--pred ...] [--country US] [--reference NAME]
                [--resamples 1000] [--seed 20261105] [--json out.json]

Only numbers are printed: counts, scores and intervals. Item texts are never written out.

Addresses. Gold items: {"id", "country", "fields": {label: text}, "parts": {label: text}}. Predictions: {"id", "fields": {...}, "parts": {...}}.
  Coarse labels are the ones every system is scored on: recipient, house_number, road, unit, po_box, city, state, postcode, country.
  Fine labels replace `road` with pre_directional, street_name, street_type, post_directional (from "parts") and are scored only for
  systems that supply parts. A value is right when its normalized text equals the gold text (accents folded, case folded, only letters
  and digits kept). Field F1 is micro-averaged over items: precision = right fields / predicted fields, recall = right fields / gold
  fields. Full-parse accuracy is the share of items whose predicted label set and every value equal the gold exactly.
Names. Gold items: {"id", "country", "text", "class", "labels": "T G F ..."} with one label per white-space-separated word
  (T title, G given, M middle, F family, S suffix, N nickname, B company base, L legal form, O other). Predictions:
  {"id", "class", "parts": {letter: [text, ...]}}. Token accuracy: the share of words of person and multiple-person items whose label
  is right, the prediction's parts being assigned to words by matching text in order (company token accuracy likewise over company items).
  Classification accuracy: the share of items whose class (person, company, multiple, unknown) is right.

Intervals: percentile bootstrap over items, `resamples` resamples with a fixed seed, the same resamples for every system, so the
difference to the reference system is a paired interval.
"""
import argparse
import json
import random
import re
import unicodedata
from collections import defaultdict

COARSE = ["recipient", "house_number", "road", "unit", "po_box", "city", "state", "postcode", "country"]
FINE_ROAD = ["pre_directional", "street_name", "street_type", "post_directional"]


def norm(s):
    s = unicodedata.normalize("NFKD", str(s).casefold())
    return "".join(c for c in s if c.isalnum() and not unicodedata.combining(c))


def address_item(gold, pred, fine):
    """Returns (right, predicted, gold fields, exact) for one item."""
    def collect(item, fine_mode):
        out = {}
        src = item.get("fields", {})
        parts = item.get("parts", {})
        for k in COARSE:
            if fine_mode and k == "road":
                for p in FINE_ROAD:
                    if parts.get(p):
                        out[p] = norm(parts[p])
                continue
            v = src.get(k)
            if v:
                out[k] = norm(v)
        return {k: v for k, v in out.items() if v}
    g = collect(gold, fine)
    p = collect(pred, fine) if pred else {}
    right = sum(1 for k, v in p.items() if g.get(k) == v)
    return right, len(p), len(g), (p == g)


def load(path):
    d = {}
    with open(path, encoding="utf-8") as f:
        for line in f:
            if line.strip():
                j = json.loads(line)
                d[j["id"]] = j
    return d


def assign(text, parts):
    """Gives each word of text the label of the first unused predicted word that matches it, O otherwise."""
    pool = []
    for label, values in parts.items():
        for v in values:
            for w in str(v).split():
                if norm(w):
                    pool.append([label, norm(w), False])
    labels = []
    for tok in text.split():
        n = norm(tok)
        lab = "O"
        for e in pool:
            if not e[2] and n and e[1] == n:
                e[2] = True
                lab = e[0]
                break
        labels.append(lab)
    return labels


def pct(sorted_vals, q):
    i = min(len(sorted_vals) - 1, max(0, int(round(q * (len(sorted_vals) - 1)))))
    return sorted_vals[i]


def bootstrap(per_item, stat, n_items, resamples, seed):
    """per_item: {system: list of per-item tuples}; stat: tuples -> float. Returns {system: (point, lo, hi)}, diffs vs first system."""
    rng = random.Random(seed)
    names = list(per_item)
    points = {s: stat(per_item[s]) for s in names}
    samples = {s: [] for s in names}
    for _ in range(resamples):
        idx = [rng.randrange(n_items) for _ in range(n_items)]
        for s in names:
            samples[s].append(stat([per_item[s][i] for i in idx]))
    res = {}
    for s in names:
        v = sorted(samples[s])
        res[s] = (points[s], pct(v, 0.025), pct(v, 0.975))
    return res, samples, points


def f1_stat(items):
    r = sum(i[0] for i in items)
    p = sum(i[1] for i in items)
    g = sum(i[2] for i in items)
    if p == 0 or g == 0 or r == 0:
        return 0.0
    prec, rec = r / p, r / g
    return 2 * prec * rec / (prec + rec)


def ratio_stat(items):
    n = sum(i[1] for i in items)
    return sum(i[0] for i in items) / n if n else 0.0


def report(rows, title, per_item, n_items, args, out):
    if not per_item:
        return
    res, samples, points = bootstrap(per_item, rows, n_items, args.resamples, args.seed)
    ref = args.reference or next(iter(per_item))
    for s, (pt, lo, hi) in res.items():
        entry = {"metric": title, "system": s, "n_items": n_items, "score": round(100 * pt, 2), "ci95": [round(100 * lo, 2), round(100 * hi, 2)]}
        if s != ref and ref in samples:
            d = sorted(a - b for a, b in zip(samples[s], samples[ref]))
            entry["vs_" + ref] = {"diff": round(100 * (points[s] - points[ref]), 2), "ci95": [round(100 * pct(d, 0.025), 2), round(100 * pct(d, 0.975), 2)]}
        out.append(entry)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("kind", choices=["address", "name"])
    ap.add_argument("--gold", required=True)
    ap.add_argument("--pred", action="append", default=[])
    ap.add_argument("--country")
    ap.add_argument("--reference")
    ap.add_argument("--resamples", type=int, default=1000)
    ap.add_argument("--seed", type=int, default=20261105)
    ap.add_argument("--json")
    args = ap.parse_args()
    gold = load(args.gold)
    ids = sorted(i for i, g in gold.items() if not args.country or g.get("country") == args.country)
    preds = {}
    for p in args.pred:
        name, path = p.split("=", 1)
        preds[name] = load(path)
    out = []
    if args.kind == "address":
        for mode in ("coarse", "fine"):
            fine = mode == "fine"
            systems = {n: d for n, d in preds.items() if not fine or any(v.get("parts") for v in d.values())}
            per = {n: [address_item(gold[i], d.get(i), fine) for i in ids] for n, d in systems.items()}
            report(f1_stat, f"address field F1 ({mode})", per, len(ids), args, out)
            full = {n: [(1 if t[3] else 0, 1) for t in v] for n, v in per.items()}
            report(ratio_stat, f"address full-parse accuracy ({mode})", full, len(ids), args, out)
    else:
        tok, ctok, cls = defaultdict(list), defaultdict(list), defaultdict(list)
        has_class = {n: any("class" in v for v in d.values()) for n, d in preds.items()}
        for n, d in preds.items():
            for i in ids:
                g = gold[i]
                words = g["text"].split()
                gl = g["labels"].split()
                p = d.get(i)
                pl = assign(g["text"], p.get("parts", {})) if p else ["O"] * len(words)
                right = sum(1 for a, b in zip(gl, pl) if a == b)
                if g["class"] in ("person", "multiple"):
                    tok[n].append((right, len(words)))
                elif g["class"] == "company":
                    ctok[n].append((right, len(words)))
                if has_class[n]:
                    cls[n].append((1 if p and p.get("class") == g["class"] else 0, 1))
        # person and company token accuracy only over the items of that class; the same item set for every system.
        report(ratio_stat, "name token accuracy (person and multiple items)", dict(tok), len(next(iter(tok.values()))) if tok else 0, args, out)
        report(ratio_stat, "company token accuracy (company items)", dict(ctok), len(next(iter(ctok.values()))) if ctok else 0, args, out)
        report(ratio_stat, "name classification accuracy (all items)", dict(cls), len(next(iter(cls.values()))) if cls else 0, args, out)
    for e in out:
        extra = ""
        k = [x for x in e if x.startswith("vs_")]
        if k:
            extra = f"  {k[0]} {e[k[0]]['diff']:+.2f} [{e[k[0]]['ci95'][0]:+.2f}, {e[k[0]]['ci95'][1]:+.2f}]"
        print(f"{e['metric']:<55} {e['system']:<16} n={e['n_items']:<4} {e['score']:6.2f} [{e['ci95'][0]:6.2f}, {e['ci95'][1]:6.2f}]{extra}")
    if args.json:
        with open(args.json, "w", encoding="utf-8") as f:
            json.dump(out, f, indent=1)


if __name__ == "__main__":
    main()
