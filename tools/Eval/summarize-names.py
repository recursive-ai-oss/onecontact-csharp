#!/usr/bin/env python3
"""Prints, per country and name metric, the score of the rules, the old and the new model, the best reference and the paired differences.

usage: summarize-names.py <score-names.sh output directory>

Aggregates only. For each row: score [95% CI] of rules / old / new; new minus rules and new minus old with their paired intervals; the best reference
system (the highest of probablepeople and nameparser on that row) and new minus it.
"""
import glob
import json
import os
import sys

d = sys.argv[1]
countries = sorted({os.path.basename(f).split("-")[0] for f in glob.glob(os.path.join(d, "*-rules.json"))})
for cc in countries:
    load = lambda ref: json.load(open(os.path.join(d, f"{cc}-{ref}.json")))
    by = {ref: load(ref) for ref in ("rules", "old", "probablepeople", "nameparser")}
    metrics = []
    for e in by["rules"]:
        if e["metric"] not in metrics:
            metrics.append(e["metric"])
    for m in metrics:
        def row(ref, system):
            for e in by[ref]:
                if e["metric"] == m and e["system"] == system:
                    return e
            return None
        sc = {s: row("rules", s) for s in ("rules", "old", "new", "probablepeople", "nameparser")}
        if sc["new"] is None:
            continue
        refs = [(s, sc[s]["score"]) for s in ("probablepeople", "nameparser") if sc[s]]
        best = max(refs, key=lambda r: r[1]) if refs else None
        fmt = lambda e: f"{e['score']:.2f} [{e['ci95'][0]:.1f},{e['ci95'][1]:.1f}]" if e else "-"
        diff_rules = sc["new"].get("vs_rules")
        e_old = row("old", "new")
        diff_old = e_old.get("vs_old") if e_old else None
        line = f"{cc} | {m[:34]:34} | n={sc['new']['n_items']:3} | rules {fmt(sc['rules'])} | old {fmt(sc['old'])} | new {fmt(sc['new'])}"
        if diff_rules:
            line += f" | new-rules {diff_rules['diff']:+.2f} [{diff_rules['ci95'][0]:+.1f},{diff_rules['ci95'][1]:+.1f}]"
        if diff_old:
            line += f" | new-old {diff_old['diff']:+.2f} [{diff_old['ci95'][0]:+.1f},{diff_old['ci95'][1]:+.1f}]"
        if best:
            e = row(best[0], "new")
            v = e.get("vs_" + best[0]) if e else None
            line += f" | best {best[0][:6]} {best[1]:.2f}"
            if v:
                line += f" new-best {v['diff']:+.2f} [{v['ci95'][0]:+.1f},{v['ci95'][1]:+.1f}]"
        print(line)
