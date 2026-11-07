#!/usr/bin/env python3
"""Checks crawled training pages against every evaluation page store and exclusion list.

usage: check-stores.py <private root> <pages.jsonl> [--list-bad <out.txt>] [--skip <folder> ...]

Uses the store listing of exclude-all-stores.py (every pages*.jsonl and exclude-hosts*.txt under the private root): prints how many pages
have a host, a site (last two labels) or a URL in it. With --list-bad the offending page ids are written (ids only). Counts only otherwise.
"""
import json
import sys

here = __file__.replace("check-stores.py", "exclude-all-stores.py")
# The listing script runs its command line when imported; run only the part that defines the helpers.
src = open(here, encoding="utf-8").read().split('if sys.argv[1] == "build"')[0]
ns = {}
exec(compile(src, "stores", "exec"), ns)

root, pages = sys.argv[1], sys.argv[2]
skip = [sys.argv[i + 1] for i, a in enumerate(sys.argv) if a == "--skip"]
# --skip leaves out folders that are not evaluation stores (another training draw's own exclusion list, which names this draw's hosts).
hosts, urls = ns["stores"](root, skip=skip)
sites = {".".join(h.split(".")[-2:]) for h in hosts}
n = bh = bs = bu = 0
bad = []
for line in open(pages, encoding="utf-8"):
    if not line.strip():
        continue
    r = json.loads(line)
    h = ns["norm"](r["host"])
    n += 1
    a, b, c = h in hosts, ".".join(h.split(".")[-2:]) in sites, r["url"] in urls
    bh += a
    bs += b
    bu += c
    if a or b or c:
        bad.append(r["id"])
print(f"store hosts {len(hosts)} urls {len(urls)}; pages {n}; host overlap {bh}; site overlap {bs}; url overlap {bu}")
if "--list-bad" in sys.argv:
    open(sys.argv[sys.argv.index("--list-bad") + 1], "w", encoding="utf-8").write("".join(i + "\n" for i in bad))
sys.exit(1 if bad else 0)
