#!/usr/bin/env python3
"""Prints the scorer's saved output for countries as one compact line per metric: system score [ci95] and the difference to the reference.

usage: summary.py <score-CC.txt> [...]      (the files run.sh output was saved to; aggregates only)
"""
import re
import sys

SHORT = {"address field F1 (coarse)": "addrF1", "address full-parse accuracy (coarse)": "addrFull",
         "name token accuracy (person and multiple items)": "personTok", "company token accuracy (company items)": "companyTok",
         "name classification accuracy (all items)": "class", "legal-form detection (company items)": "legalForm"}
ROW = re.compile(r"^(?P<m>.+?)\s{2,}(?P<s>[\w-]+)\s+n=(?P<n>\d+)\s+(?P<v>[\d.]+) \[\s*(?P<lo>[\d.]+),\s*(?P<hi>[\d.]+)\](?:\s+vs_\w+ (?P<d>[+-][\d.]+) \[(?P<dlo>[+-][\d.]+), (?P<dhi>[+-][\d.]+)\])?")
for path in sys.argv[1:]:
    cc = re.search(r"score-(\w+)\.txt", path).group(1)
    rows = {}
    for line in open(path, encoding="utf-8"):
        m = ROW.match(line.rstrip())
        if not m or m["m"] not in SHORT:
            continue
        d = f" ({m['d']} [{m['dlo']},{m['dhi']}])" if m["d"] else ""
        rows.setdefault(SHORT[m["m"]], []).append(f"{m['s']} {m['v']} [{m['lo']},{m['hi']}]{d}")
        rows[SHORT[m["m"]] + "#n"] = m["n"]
    for k in SHORT.values():
        print(f"{cc} {k} n={rows.get(k + '#n')}: " + "; ".join(rows.get(k, [])))
