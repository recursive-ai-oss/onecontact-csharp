#!/usr/bin/env python3
"""usage: compare.py <before summary.txt> <after summary.txt> [hint|nohint]
Prints, per country, every-field %, field % (found/seen), refused, wrong-country and undetermined counts, before -> after, for the runs with or without a hint."""
import re
import sys


def parse(path):
    rows = {}
    for line in open(path, encoding="utf-8"):
        m = re.match(r"(\w\w) model=(\w+) hint=(yes|no)\s+f1=\S+ p=\S+ r=\S+ n=(\d+) every-field=\d+ \(([\d.]+)%\) no-extra=\d+ \([\d.]+%\) fields=\d+/\d+ \(([\d.]+)%\) refused=(\d+) wrong-country=(\d+)(?: undetermined=(\d+))?", line)
        if m:
            rows[(m[1], m[3])] = (float(m[5]), float(m[6]), int(m[7]), int(m[8]), int(m[9] or 0))
    return rows


b, a = parse(sys.argv[1]), parse(sys.argv[2])
mode = "yes" if len(sys.argv) > 3 and sys.argv[3] == "hint" else "no"
print(f"hint={mode}: cc  every-field%   field%    refused   wrong   undetermined")
for cc, h in sorted(b):
    if h != mode or (cc, h) not in a:
        continue
    x, y = b[(cc, h)], a[(cc, h)]
    print(f"{cc}  {x[0]:5.1f}->{y[0]:5.1f}  {x[1]:5.1f}->{y[1]:5.1f}  {x[2]:4d}->{y[2]:4d}  {x[3]:3d}->{y[3]:3d}  {y[4]:4d}")
