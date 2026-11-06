#!/usr/bin/env python3
"""Builds the gold files of one Tier 2 group B country from its drafts, the second pre-label, the reviewer's corrections and the rule
parser's own output, and prints the label provenance counts.

usage: gold.py <set root> <country> <number of address drafts reviewed> [<labeler>]

Addresses: the reviewer read draft ids 0..N-1 (the rest are left out). The pre-label is prelabel.py's; `rules_agreed` is recomputed against
the rule parser's prediction (pred-address-<CC>.jsonl) as the scorer sees it, so it says whether the rules alone had the same fields.
Names: the rule parser's pre-label plus corrections, with the `keep` list of corr.py. Items are corrected through labeling.py apply.
Writes <set root>/sets/gold-address-<CC>.jsonl and gold-name-<CC>.jsonl (not yet frozen: freeze-sets.py does that).
"""
import json
import os
import subprocess
import sys

here = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(here, ".."))
import score  # noqa: E402

root, cc, n_addr = sys.argv[1], sys.argv[2], int(sys.argv[3])
who = sys.argv[4] if len(sys.argv) > 4 else "agent adjudication"
d, out = os.path.join(root, "drafts"), os.path.join(root, "sets")
os.makedirs(out, exist_ok=True)
for kind in ("address", "name"):
    open(os.path.join(d, f"corr-{kind}-{cc}.jsonl"), "a").close()
lab = os.path.join(here, "..", "labeling.py")
pre = f"pre2-address-{cc}.jsonl"
subprocess.run(["python3", lab, "apply", "address", f"{d}/draft-address-{cc}.jsonl", f"{d}/{pre}", f"{d}/corr-address-{cc}.jsonl", f"{out}/gold-address-{cc}.jsonl", who], check=True)
subprocess.run(["python3", lab, "apply", "name", f"{d}/draft-name-{cc}.jsonl", f"{d}/pred-name-{cc}.jsonl", f"{d}/corr-name-{cc}.jsonl", f"{out}/gold-name-{cc}.jsonl", who], check=True)

rules = {}
for l in open(f"{d}/pred-address-{cc}.jsonl", encoding="utf-8"):
    j = json.loads(l)
    rules[j["id"]] = j
rows, agreed = [], 0
for l in open(f"{out}/gold-address-{cc}.jsonl", encoding="utf-8"):
    r = json.loads(l)
    if int(r["id"].rsplit("-", 1)[1]) >= n_addr:
        continue
    g = {k: score.norm(v) for k, v in r["fields"].items() if v}
    p = {k: score.norm(v) for k, v in rules.get(r["id"], {}).get("fields", {}).items() if v}
    r["rules_agreed"] = g == p
    r["parts"] = {}
    agreed += r["rules_agreed"]
    rows.append(r)
with open(f"{out}/gold-address-{cc}.jsonl", "w", encoding="utf-8") as f:
    for r in rows:
        f.write(json.dumps(r, ensure_ascii=False) + "\n")
print(f"{cc} addresses {len(rows)}: rules agreed {agreed}, differed {len(rows) - agreed}")
