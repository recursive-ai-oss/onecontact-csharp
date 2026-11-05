#!/usr/bin/env python3
"""Freezes an extraction evaluation set: writes the gold file in canonical form and a manifest, and prints aggregate counts.

usage: freeze.py <gold.jsonl> <pre.jsonl> <out-dir> <name> [<set-dir>]

Canonical form: one JSON object per line, sorted by id, keys sorted, compact separators, UTF-8. The manifest (<out-dir>/manifest.json)
lists each frozen file with its role (`eval`: training code must load only `train` and `dev` files), item count and SHA-256; for a
web set it also records the SHA-256 of every stored page, so the labels can be tied to the exact bytes they count offsets in.
Only aggregates are printed: item counts, field counts per type, how many pre-labelled fields the review kept, dropped or added.
"""
import hashlib
import json
import os
import sys
from collections import Counter

gold_path, pre_path, out, name = sys.argv[1:5]
set_dir = sys.argv[5] if len(sys.argv) > 5 else None
os.makedirs(out, exist_ok=True)
items = sorted((json.loads(l) for l in open(gold_path, encoding="utf-8") if l.strip()), key=lambda r: r["id"])
pre = {}
for l in open(pre_path, encoding="utf-8"):
    r = json.loads(l)
    pre[r["id"]] = r

body = "".join(json.dumps(r, sort_keys=True, separators=(",", ":"), ensure_ascii=False) + "\n" for r in items).encode("utf-8")
path = os.path.join(out, f"gold-{name}.jsonl")
with open(path, "wb") as f:
    f.write(body)
digest = hashlib.sha256(body).hexdigest()

manifest_path = os.path.join(out, "manifest.json")
manifest = json.load(open(manifest_path)) if os.path.exists(manifest_path) else {"files": {}}
entry = {"role": "eval", "items": len(items), "sha256": digest}
if set_dir:
    pages = {}
    for r in items:
        pages[r["id"]] = hashlib.sha256(open(os.path.join(set_dir, r["file"]), "rb").read()).hexdigest()
    entry["pages_sha256"] = pages
manifest["files"][f"gold-{name}.jsonl"] = entry
json.dump(manifest, open(manifest_path, "w"), indent=1, sort_keys=True)

types = Counter()
kept = dropped = added = 0
for r in items:
    for f in r["fields"]:
        types[f["type"]] += 1
        added += 0 if f.get("pre") else 1
    p = pre[r["id"]]
    kept += sum(1 for f in r["fields"] if f.get("pre"))
    dropped += sum(1 for f in p["fields"] if f["start"] >= 0) - sum(1 for f in r["fields"] if f.get("pre"))
print(f"{name}: {len(items)} items, sha256 {digest}")
print("  agreed (pre-label accepted unchanged):", sum(1 for r in items if r.get("rules_agreed")), "of", len(items))
print("  country:", dict(Counter(r["country"] for r in items)))
print("  group:", dict(Counter(r.get("group") or r.get("stratum") for r in items)))
print("  fields:", dict(types), "total", sum(types.values()))
print(f"  pre-labelled fields kept {kept}, dropped {dropped}; fields added by the reviewer {added}")
print("  items with no gold field:", sum(1 for r in items if not r["fields"]))
