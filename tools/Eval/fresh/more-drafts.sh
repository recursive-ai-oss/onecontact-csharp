#!/usr/bin/env bash
# Adds more address drafts for one country from the pages already stored by draw-addresses.sh, when the first draft left too few items after review.
# usage: more-drafts.sh <set root> <rules dll> <CC> <seed> <count>
# A new draw (another seed) is made from the same pages and every text already drafted is filtered out; its items are numbered after the existing ones,
# appended to drafts/draft-address-<CC>.jsonl, and the rules' hinted pre-label is rebuilt for the whole file (it is deterministic, so earlier
# items keep their pre-labels and the corrections written for them still apply).
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
root=${1:?set root}; dll=${2:?rules dll}; cc=${3:?country}; seed=${4:?seed}; n=${5:?count}
cd "$root"
tmp=drafts/more-$cc
mkdir -p "$tmp"
case $cc in
  ES|CZ|SK|HR|IM)
    python3 "$here/../tier2-south/extract-candidates.py" "$cc" "$cc/pages.jsonl" "$tmp" "$seed" "$n" 0 0 ;;
  DE|BE|CH)
    mkdir -p "$tmp/t2"; ln -sf "$root/t2/pages.jsonl" "$tmp/t2/pages.jsonl"
    python3 "$here/../crawl/extract-candidates-t2.py" "$tmp/t2" "$seed" "$cc" "$n" 0 0
    mv "$tmp/t2/draft-address-$cc.jsonl" "$tmp/" ;;
esac
python3 - "$cc" "$tmp/draft-address-$cc.jsonl" "drafts/draft-address-$cc.jsonl" <<'EOF'
import json, re, sys
cc, new, old = sys.argv[1:4]
have = [json.loads(l) for l in open(old, encoding="utf-8") if l.strip()]
nxt = max(int(r["id"].rsplit("-", 1)[1]) for r in have) + 1
key = lambda r: re.sub(r"\W+", "", r["text"].lower())
seen = {key(r) for r in have}
with open(old, "a", encoding="utf-8") as f:
    for l in open(new, encoding="utf-8"):
        if l.strip():
            r = json.loads(l)
            if key(r) in seen:  # the extractor only avoids repeats within one draw, so earlier texts are filtered here
                continue
            seen.add(key(r))
            r["id"] = f"addr-{cc}-{nxt:03d}"
            nxt += 1
            f.write(json.dumps(r, ensure_ascii=False) + "\n")
print("drafts now", nxt)
EOF
dotnet "$dll" address "drafts/draft-address-$cc.jsonl" "drafts/pred-address-$cc.jsonl" hint
