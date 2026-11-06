#!/usr/bin/env python3
"""Turns a compact adjudication spec into the corrections files that labeling.py apply reads.

usage: corr.py <country> <drafts dir> < spec

Spec lines (the id suffix is what follows "addr-<CC>-" or "name-<CC>-"):
  addr 044 drop                               drop the item
  addr 044 no=51|rd=ul. Zielona|ct=Choroszcz  edit the rules' pre-label (keys as in labeling.py: no rd pt un box rcp ct st pc cy)
  addr 087 -rcp                               remove fields from the pre-label (several: -rcp|-un)
  addr 044 =no=51|rd=...                      replace the pre-label with exactly these fields
  name keep p000 p002 c010                    keep only these name items
  name p000 person T G F                      set the class and one label per word (T G M F S N B L O)
Writes corr-address-<CC>.jsonl and corr-name-<CC>.jsonl in the drafts directory. Appends: run once per batch of lines, in order.
"""
import json
import os
import sys

cc, d = sys.argv[1], sys.argv[2]
addr, name = [], []
keep = []
for line in sys.stdin:
    line = line.rstrip("\n")
    if not line.strip():
        continue
    kind, rest = line.split(" ", 1)
    if kind == "addr":
        suf, spec = rest.split(" ", 1)
        item = {"id": f"addr-{cc}-{suf}"}
        if spec.strip() == "drop":
            item["drop"] = True
        else:
            replace = spec.startswith("=")
            spec = spec.lstrip("=")
            if not replace:
                item["+"] = True
            for kv in spec.split("|"):
                if kv.startswith("-"):
                    item.setdefault("-", []).append(kv[1:])
                else:
                    k, v = kv.split("=", 1)
                    item[k] = v
        addr.append(item)
    else:
        parts = rest.split()
        if parts[0] == "keep":
            keep += parts[1:]
        else:
            name.append({"id": f"name-{cc}-{parts[0]}", "class": parts[1], "labels": " ".join(parts[2:])})
if keep:
    name.append({"keep": keep})
for fn, rows in ((f"corr-address-{cc}.jsonl", addr), (f"corr-name-{cc}.jsonl", name)):
    with open(os.path.join(d, fn), "a", encoding="utf-8") as f:
        for r in rows:
            f.write(json.dumps(r, ensure_ascii=False) + "\n")
print(cc, "address corrections", len(addr), "name corrections", len(name), "keep", len(keep))
