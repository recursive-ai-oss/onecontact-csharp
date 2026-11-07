#!/usr/bin/env python3
"""Tabulates the scores written by run-challenger.sh: one row per country, mode (hint or nohint) and metric (coarse field F1, full-parse accuracy)
with each system's score and 95% interval and each challenger's paired difference to the default with its interval. Aggregates only.

usage: challenger-summary.py <out directory>
"""
import json
import os
import sys

METRICS = {"address field F1 (coarse)": "field-F1", "address full-parse accuracy (coarse)": "full-parse"}


def main():
    root = sys.argv[1]
    print("\t".join(["country", "mode", "metric", "n", "default", "c1", "c1-default", "c2", "c2-default"]))
    for cc in sorted(os.listdir(root)):
        for mode in ("hint", "nohint"):
            path = os.path.join(root, cc, f"scores-{mode}.json")
            if not os.path.isfile(path):
                continue
            with open(path, encoding="utf-8") as f:
                entries = json.load(f)
            for metric, short in METRICS.items():
                by = {e["system"]: e for e in entries if e["metric"] == metric}
                if "default" not in by:
                    continue
                row = [cc, mode, short, str(by["default"]["n_items"]), cell(by["default"])]
                for s in ("c1", "c2"):
                    row.append(cell(by[s]))
                    d = by[s]["vs_default"]
                    row.append(f"{d['diff']:+.2f} [{d['ci95'][0]:+.2f}, {d['ci95'][1]:+.2f}]")
                print("\t".join(row))


def cell(e):
    return f"{e['score']:.2f} [{e['ci95'][0]:.2f}, {e['ci95'][1]:.2f}]"


if __name__ == "__main__":
    main()
