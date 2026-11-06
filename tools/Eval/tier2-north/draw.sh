#!/usr/bin/env bash
# Draws the address and name drafts of the Tier 2 north countries (DK SE FI NO IS EE LV LT) from stored pages and writes the rules' pre-labels.
# usage: draw.sh <private work dir> <rules dll> [<crawl id>]
#   Steps: select-pages.py once per country TLD (random index blocks, hosts of earlier sets excluded via <dir>/hosts-used.txt), merge the
#   candidate files into <dir>/candidates.jsonl, then ../crawl/fetch-pages.py <dir> 1000000 2 (at most two threads), then this script.
#   Adjudication then uses ../tier2-south/corr.py and gold.sh (set root = <dir>, drafts in <dir>/drafts), freezing uses ../freeze-sets.py,
#   scoring uses ../tier2-south/run.sh.
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
work=${1:?work dir}
dll=${2:?rules dll}
mkdir -p "$work/drafts"
for t in dk se fi no is ee lv lt; do
  c=${t^^}
  python3 "$here/extract-candidates.py" "$work" 1 $t 110 70 40 | sed -n '1p'
  mv "$work/draft-address-$c.jsonl" "$work/draft-name-$c.jsonl" "$work/drafts/"
  dotnet "$dll" address "$work/drafts/draft-address-$c.jsonl" "$work/drafts/pred-address-$c.jsonl" hint
  dotnet "$dll" name "$work/drafts/draft-name-$c.jsonl" "$work/drafts/pred-name-$c.jsonl"
done
