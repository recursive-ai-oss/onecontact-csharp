#!/usr/bin/env python3
"""Fixes an evaluation set: rewrites each gold file in canonical form (lines sorted by id, keys sorted, compact JSON) and writes a
manifest with each file's role, item count and SHA-256, plus the hashes of the page store the sets were drawn from.

usage: freeze-sets.py <set directory> <crawl id> <pages.jsonl> [<pages.jsonl> ...]

The manifest holds only counts and hashes. Training code loads files by role and must refuse role `eval`.
"""
import glob
import hashlib
import json
import os
import sys

d, crawl = sys.argv[1], sys.argv[2]
manifest = {"crawl": crawl, "files": {}, "page_stores": {}}
for p in sys.argv[3:]:
    manifest["page_stores"][os.path.basename(os.path.dirname(p))] = hashlib.sha256(open(p, "rb").read()).hexdigest()
for path in sorted(glob.glob(os.path.join(d, "gold-*.jsonl"))):
    rows = [json.loads(l) for l in open(path, encoding="utf-8") if l.strip()]
    rows.sort(key=lambda r: r["id"])
    text = "".join(json.dumps(r, sort_keys=True, ensure_ascii=False, separators=(",", ":")) + "\n" for r in rows)
    open(path, "w", encoding="utf-8").write(text)
    manifest["files"][os.path.basename(path)] = {"role": "eval", "items": len(rows), "sha256": hashlib.sha256(text.encode("utf-8")).hexdigest()}
json.dump(manifest, open(os.path.join(d, "MANIFEST.json"), "w"), indent=1)
for k, v in manifest["files"].items():
    print(k, v["items"], v["sha256"])
