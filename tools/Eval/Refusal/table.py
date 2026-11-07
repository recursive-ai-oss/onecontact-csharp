#!/usr/bin/env python3
"""Turns refusal.tsv (counts only) into the markdown tables of docs/maintainers/refusal-rows.md.

usage: table.py <refusal.tsv>
"""
import collections
import sys

rows = collections.OrderedDict()
reasons = {"hint": collections.Counter(), "nohint": collections.Counter()}
total = {"hint": collections.Counter(), "nohint": collections.Counter()}
with open(sys.argv[1], encoding="utf-8") as f:
    next(f)
    for line in f:
        p = line.rstrip("\n").split("\t")
        key, mode = p[0], p[1]
        n, refused, nocountry, right, overseas, wrong, invented, offsets = (int(x) for x in p[2:10])
        rows.setdefault(key, {})[mode] = (n, refused, nocountry, right, overseas, wrong, invented, offsets)
        for item in filter(None, p[10].split(";")) if len(p) > 10 else []:
            r, c = item.rsplit("=", 1)
            reasons[mode][r] += int(c)
        for name, v in zip(("items", "refused", "nocountry", "right", "overseas", "wrong", "invented", "offsets"), (n, refused, nocountry, right, overseas, wrong, invented, offsets)):
            total[mode][name] += v

print("| Set / country | Items | Hinted: refused | read, right | read, wrong country | invented | No hint: refused | read, no country | read, right | read, wrong country | invented |")
print("|---|---|---|---|---|---|---|---|---|---|---|")
for key, m in rows.items():
    h, u = m["hint"], m["nohint"]
    print(f"| {key} | {h[0]} | {h[1]} | {h[3] + h[4]} | {h[5]} | {h[6]} | {u[1]} | {u[2]} | {u[3] + u[4]} | {u[5]} | {u[6]} |")
h, u = total["hint"], total["nohint"]
print(f"| **All** | {h['items']} | {h['refused']} | {h['right'] + h['overseas']} | {h['wrong']} | {h['invented']} | {u['refused']} | {u['nocountry']} | {u['right'] + u['overseas']} | {u['wrong']} | {u['invented']} |")
print()
print("| Reason | Hinted | No hint |")
print("|---|---|---|")
for r in sorted(set(reasons["hint"]) | set(reasons["nohint"]), key=lambda r: -(reasons["hint"][r] + reasons["nohint"][r])):
    print(f"| `{r}` | {reasons['hint'][r]} | {reasons['nohint'][r]} |")
