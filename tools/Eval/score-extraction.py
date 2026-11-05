#!/usr/bin/env python3
"""Scores contact-extraction predictions against a gold set: field F1 per type, record F1, paired bootstrap 95% intervals.

usage: score-extraction.py --gold gold.jsonl --pred NAME=pred.jsonl [--pred ...] [--produces NAME=phone,email ...]
                           [--mentions all|text] [--reference NAME] [--resamples 1000] [--seed 20261105] [--json out.json]

Only numbers are printed: counts, scores and intervals. Item texts are never written out.

Gold items: {"id", "fields": [{"id", "type", "start", "end", "text"}], "records": [[field id, ...]]}. Predictions:
{"id", "fields": [{"type", "start", "end", "text"}], "records": [[field index, ...]]} (records only for systems that group fields).
Types: person, org, address, phone, email, website. Offsets are bytes of the stored page for pages and UTF-16 code units for text.
Every mention in the text or markup is one field, so a phone number written in the header and the footer is two fields.

Field F1, two ways, micro-averaged over items, per type:
  span   a predicted field is right when its type, start and end equal a gold field's;
  value  the fields of an item are compared as sets of (type, normalized text): phones by their digits (a leading 1 dropped from eleven),
         emails lowercased, websites without scheme, `www.`, query, fragment and trailing slash, everything else folded to letters and
         digits. A value written twice counts once.
Record F1 (systems that supply records): a record is its set of (type, normalized text); a predicted record is right when it equals
  a gold record exactly (each gold record matched at most once); precision = right / predicted records, recall = right / gold records.
  Link F1 is the softer pair measure: the unordered pairs of distinct values that share a record.
A system is scored only on the types it produces (--produces; default all six); `all` pools those types.

Mentions: `all` scores every field, markup included (a JSON-LD value, a `mailto:` link). `text` keeps only fields read from the
visible text (gold fields whose `src` is Text; predictions without a `src`, or with Text), which is what the reference systems can
see; our own markup fields are left out of that run.

Intervals: percentile bootstrap over items, `resamples` resamples with a fixed seed, the same resamples for every system, so the
difference to the reference system is a paired interval.
"""
import argparse
import itertools
import json
import os
import re
import sys
import unicodedata

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import score  # noqa: E402  (the bootstrap and F1 helpers of the address and name scorer)

TYPES = ["person", "org", "address", "phone", "email", "website"]


def fold(s):
    s = unicodedata.normalize("NFKD", str(s).casefold())
    return "".join(c for c in s if c.isalnum() and not unicodedata.combining(c))


def key(type_, text):
    """The normalized comparison text of one mention."""
    if type_ == "phone":
        body = re.split(r"(?i)\b(?:ext|extension|x)\b|#", text)[0]
        d = re.sub(r"\D", "", body)
        return d[1:] if len(d) == 11 and d[0] == "1" else d
    if type_ == "email":
        return re.sub(r"(?i)^mailto:", "", text.strip()).strip(" .,;:<>()[]\"'").lower()
    if type_ == "website":
        u = text.strip().strip(" .,;:<>()[]\"'").lower()
        u = re.sub(r"^[a-z][a-z0-9+.-]*://", "", u)
        u = re.sub(r"^www\.", "", u)
        u = re.split(r"[?#]", u)[0]
        return u.rstrip("/")
    return fold(text)


def load(path):
    out = {}
    with open(path, encoding="utf-8") as f:
        for line in f:
            if line.strip():
                row = json.loads(line)
                out[row["id"]] = row
    return out


def record_sets(item, id_of):
    """Records as sets of (type, key); `id_of` maps the record's member ids to field dicts."""
    sets = []
    for rec in item.get("records", []):
        members = frozenset((id_of[m]["type"], key(id_of[m]["type"], id_of[m]["text"])) for m in rec if m in id_of)
        members = frozenset(m for m in members if m[1])
        if members:
            sets.append(members)
    return sets


def gold_view(item):
    ids = {f["id"]: f for f in item.get("fields", [])}
    spans = {(f["type"], f["start"], f["end"]) for f in item.get("fields", [])}
    values = {(f["type"], key(f["type"], f["text"])) for f in item.get("fields", [])}
    values = {v for v in values if v[1]}
    return ids, spans, values, record_sets(item, ids)


def pred_view(item):
    fields = item.get("fields", []) if item else []
    ids = dict(enumerate(fields))
    spans = {(f["type"], f["start"], f["end"]) for f in fields if f["start"] >= 0}
    values = {(f["type"], key(f["type"], f["text"])) for f in fields}
    values = {v for v in values if v[1]}
    return ids, spans, values, record_sets(item or {}, ids) if item and "records" in item else None


def pairs(sets):
    out = set()
    for s in sets:
        out.update(frozenset(p) for p in itertools.combinations(sorted(s), 2))
    return out


def counts(pred, gold, types):
    p = {x for x in pred if x[0] in types}
    g = {x for x in gold if x[0] in types}
    return (len(p & g), len(p), len(g))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--gold", required=True)
    ap.add_argument("--pred", action="append", default=[])
    ap.add_argument("--produces", action="append", default=[])
    ap.add_argument("--mentions", choices=["all", "text"], default="all")
    ap.add_argument("--reference")
    ap.add_argument("--resamples", type=int, default=1000)
    ap.add_argument("--seed", type=int, default=20261105)
    ap.add_argument("--json")
    args = ap.parse_args()
    gold = load(args.gold)
    keep = (lambda f: f.get("src", "Text") == "Text") if args.mentions == "text" else (lambda f: True)
    for row in gold.values():
        row["fields"] = [f for f in row.get("fields", []) if keep(f)]
        kept = {f["id"] for f in row["fields"]}
        row["records"] = [[m for m in r if m in kept] for r in row.get("records", [])]
    ids = sorted(gold)
    gviews = {i: gold_view(gold[i]) for i in ids}
    preds = {}
    for p in args.pred:
        name, path = p.split("=", 1)
        preds[name] = load(path)
        for row in preds[name].values():
            if "fields" in row:
                old = row["fields"]
                row["fields"] = [f for f in old if keep(f)]
                if "records" in row:  # renumber the members that survive
                    new_of = {i: j for j, i in enumerate(i for i, f in enumerate(old) if keep(f))}
                    row["records"] = [[new_of[m] for m in r if m in new_of] for r in row["records"]]
    produces = {n: set(TYPES) for n in preds}
    for p in args.produces:
        name, ts = p.split("=", 1)
        produces[name] = set(ts.split(","))
    pviews = {n: {i: pred_view(d.get(i)) for i in ids} for n, d in preds.items()}
    out = []
    for kind, idx in (("span", 1), ("value", 2)):
        for t in TYPES + ["all"]:
            per = {}
            for n in preds:
                types = produces[n] if t == "all" else ({t} if t in produces[n] else set())
                if not types:
                    continue
                per[n] = [counts(pviews[n][i][idx], gviews[i][idx], types) for i in ids]
            before = len(out)
            score.report(score.f1_stat, f"{t} {kind} F1", per, len(ids), args, out)
            for e in out[before:]:
                rows = per[e["system"]]
                tp, np_, ng = (sum(r[i] for r in rows) for i in range(3))
                e["precision"] = round(100 * tp / np_, 2) if np_ else None
                e["recall"] = round(100 * tp / ng, 2) if ng else None
                e["gold"], e["predicted"] = ng, np_
    rec = {n: v for n, v in pviews.items() if any(x[3] is not None for x in v.values())}
    if rec:
        per_rec, per_link = {}, {}
        for n, v in rec.items():
            rows, links = [], []
            for i in ids:
                gs, ps = gviews[i][3], v[i][3] or []
                unmatched = list(gs)
                right = 0
                for s in ps:
                    if s in unmatched:
                        unmatched.remove(s)
                        right += 1
                rows.append((right, len(ps), len(gs)))
                gp, pp = pairs(gs), pairs(ps)
                links.append((len(gp & pp), len(pp), len(gp)))
            per_rec[n], per_link[n] = rows, links
        score.report(score.f1_stat, "record F1", per_rec, len(ids), args, out)
        score.report(score.f1_stat, "link F1", per_link, len(ids), args, out)
    for e in out:
        extra = ""
        k = [x for x in e if x.startswith("vs_")]
        if k:
            extra = f"  {k[0]} {e[k[0]]['diff']:+.2f} [{e[k[0]]['ci95'][0]:+.2f}, {e[k[0]]['ci95'][1]:+.2f}]"
        pr = f"  P {e['precision']} R {e['recall']} (gold {e['gold']}, predicted {e['predicted']})" if "precision" in e else ""
        print(f"{e['metric']:<22} {e['system']:<16} n={e['n_items']:<4} {e['score']:6.2f} [{e['ci95'][0]:6.2f}, {e['ci95'][1]:6.2f}]{extra}{pr}")
    # counts of gold and predicted fields, for reading the rows.
    for t in TYPES:
        print(f"gold {t}: {sum(1 for i in ids for f in gold[i].get('fields', []) if f['type'] == t)} fields")
    if args.json:
        with open(args.json, "w", encoding="utf-8") as f:
            json.dump(out, f, indent=1)


if __name__ == "__main__":
    main()
