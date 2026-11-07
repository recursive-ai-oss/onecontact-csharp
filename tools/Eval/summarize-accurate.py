#!/usr/bin/env python3
"""Condenses the text rows written by score-accurate.sh into one line per country: score and the 95% interval of the difference to the reference.
usage: summarize-accurate.py <output directory of score-accurate.sh>   Aggregates only."""
import glob
import os
import re
import sys

ROW = re.compile(r'^(?P<metric>.+?)\s{2,}(?P<sys>[\w-]+)\s+n=(?P<n>\d+)\s+(?P<score>[\d.]+) \[\s*[\d.]+,\s*[\d.]+\](?:\s+vs_(?P<ref>\w+) (?P<d>[+-][\d.]+) \[(?P<lo>[+-][\d.]+), (?P<hi>[+-][\d.]+)\])?')
WANT = {
    'address field F1 (coarse)': 'addrF1',
    'address full-parse accuracy (coarse)': 'addrFP',
    'name token accuracy (person and multiple items)': 'person',
    'company token accuracy (company items)': 'company',
    'name classification accuracy (all items)': 'class',
    'legal-form detection (company items)': 'legal',
}
out = sys.argv[1]
files = sorted(glob.glob(os.path.join(out, '*.txt')) + glob.glob(os.path.join(out, 'names-*', '*.txt')))
print('country\tmetric\tn\tshipped\taccurate\taccurate-vs-shipped\tbest reference\tbest reference score\tfirst reference: accurate minus it [95% CI]')
for f in files:
    cc = os.path.basename(f)[:-4]
    rows = {}
    for line in open(f):
        m = ROW.match(line.rstrip())
        if not m or m['metric'].strip() not in WANT:
            continue
        rows[(WANT[m['metric'].strip()], m['sys'])] = m
    for metric in ('addrF1', 'addrFP', 'person', 'company', 'class', 'legal'):
        for suffix in (('', '-hint') if metric.startswith('addr') else ('',)):
            sh, ac = rows.get((metric, 'shipped' + suffix)), rows.get((metric, 'accurate' + suffix))
            if not sh or not ac:
                continue
            best = max(((float(rows[(metric, r)]['score']), r) for r in ('libpostal', 'senzing', 'probablepeople', 'nameparser', 'cleanco') if (metric, r) in rows), default=None)
            # The reference columns are the row named in the vs_ part of the accurate line.
            vs = ac['ref']
            print('\t'.join([cc, metric + suffix, ac['n'], sh['score'], ac['score'], f"{float(ac['score']) - float(sh['score']):+.2f}",
                             best[1] if best else '', f'{best[0]:.2f}' if best else '', f"{vs}: {ac['d']} [{ac['lo']}, {ac['hi']}]" if vs else '']))
