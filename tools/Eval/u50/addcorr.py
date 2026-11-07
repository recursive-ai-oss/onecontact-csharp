#!/usr/bin/env python3
"""Turns a short reviewer's list into labeling.py corrections for an address draft.

usage: addcorr.py <corrections.jsonl to append to> <country> < list
Each input line is `<number> drop` or `<number> key=value ;; key=value ...` with the keys of labeling.py (no, rd, un, box, rcp, ct, st, pc, cy);
the number is the item's position suffix in the draft id (addr-<country>-<number>). Lines starting with # are ignored.
"""
import json
import sys

out, cc = sys.argv[1], sys.argv[2]
with open(out, "a", encoding="utf-8") as f:
    for line in sys.stdin:
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        num, rest = line.split(" ", 1)
        row = {"id": f"addr-{cc}-{int(num):03d}"}
        if rest.strip() == "drop":
            row["drop"] = True
        else:
            for kv in rest.split(";;"):
                k, v = kv.strip().split("=", 1)
                row[k.strip()] = v.strip()
        f.write(json.dumps(row, ensure_ascii=False) + "\n")
