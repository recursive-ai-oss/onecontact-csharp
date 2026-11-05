#!/usr/bin/env python3
"""Converts the committed synthetic gold files (testdata/addresses/parse-gold.tsv, testdata/names/gold.tsv) into the evaluation-set
JSONL format, so the whole pipeline (rules runner, oracle drivers, scorer) can be exercised without any real data.

usage: synthetic-sets.py <repo root> <out directory>
"""
import json
import os
import sys

root, out = sys.argv[1], sys.argv[2]
os.makedirs(out, exist_ok=True)
m = {"house_number": "house_number", "locality": "city", "state": "state", "postal": "postcode", "box": "po_box", "unit": "unit"}
with open(os.path.join(out, "synthetic-address.jsonl"), "w", encoding="utf-8") as f:
    for line in open(os.path.join(root, "testdata/addresses/parse-gold.tsv"), encoding="utf-8"):
        if line.startswith("#"):
            continue
        r = line.rstrip("\n").split("\t")
        g = dict(p.split("=", 1) for p in r[3].split(";"))
        fields = {m[k]: v for k, v in g.items() if k in m}
        keys = ("pre_directional", "street_name", "street_type", "post_directional")
        road = " ".join(g[k] for k in keys if k in g)
        if road:
            fields["road"] = road
        f.write(json.dumps({"id": r[0], "country": r[1], "text": r[2].replace("\\n", "\n"), "fields": fields, "parts": {k: g[k] for k in keys if k in g}}) + "\n")
with open(os.path.join(out, "synthetic-name.jsonl"), "w", encoding="utf-8") as f:
    rows = [l.rstrip("\n").split("\t") for l in open(os.path.join(root, "testdata/names/gold.tsv"), encoding="utf-8")][1:]
    for r in rows:
        hint = r[2]
        country = hint.split("/")[0] if hint[:2].isupper() else ""
        f.write(json.dumps({"id": r[0], "country": country if len(country) == 2 else "US", "lang": r[1], "text": r[4], "class": r[3], "labels": r[5]}, ensure_ascii=False) + "\n")
print("ok")
