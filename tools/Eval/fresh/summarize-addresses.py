#!/usr/bin/env python3
"""Condenses the logs of score-addresses.sh into one table per metric: for each country, the no-hint systems with their difference to the
no-country libpostal, and the hinted systems with their difference to the country-name libpostal and (as the keyword changes nothing and the
appended name sometimes hurts the parser) to the no-country libpostal. Aggregates only.

usage: summarize-addresses.py <log directory> <CC> [<CC> ...]
"""
import re
import sys

ROW = re.compile(r"^address (field F1|full-parse accuracy) \(coarse\)\s+(\S+)\s+n=(\d+)\s+([\d.]+) \[\s*([\d.]+),\s*([\d.]+)\](?:\s+vs_\S+ ([+-][\d.]+) \[([+-][\d.]+), ([+-][\d.]+)\])?")
NOHINT = ["rules", "model", "libpostal", "senzing"]
HINT = ["rules-hint", "model-hint", "libpostal-country", "libpostal-name", "senzing-name"]


def read(path):
    out = {}
    for line in open(path, encoding="utf-8"):
        m = ROW.match(line)
        if m:
            metric, system, n, score, lo, hi, d, dlo, dhi = m.groups()
            out[(metric, system)] = (int(n), float(score), float(lo), float(hi), d and (float(d), float(dlo), float(dhi)))
    return out


log, countries = sys.argv[1], sys.argv[2:]
for metric in ("field F1", "full-parse accuracy"):
    print(f"== {metric} (coarse): score [95% CI]  and  difference to the reference [95% CI]")
    for cc in countries:
        a = read(f"{log}/score-{cc}-vs-libpostal.txt")
        b = read(f"{log}/score-{cc}-vs-libpostal-name.txt")
        n = a[(metric, "rules")][0]
        print(f"-- {cc} n={n}")
        for title, names, src, ref in (("no hint, vs libpostal without country", NOHINT, a, "libpostal"),
                                       ("hinted, vs libpostal with country name", HINT, b, "libpostal-name"),
                                       ("hinted, vs libpostal without country", HINT, a, "libpostal")):
            cells = []
            for s in names:
                r = src.get((metric, s))
                if not r:
                    continue
                cells.append(f"{s} {r[1]:.2f} [{r[2]:.1f},{r[3]:.1f}]" + ("" if s == ref or not r[4] else f" {r[4][0]:+.2f} [{r[4][1]:+.1f},{r[4][2]:+.1f}]"))
            print(f"   {title}: " + "; ".join(cells))
