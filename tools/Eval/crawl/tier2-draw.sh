#!/usr/bin/env bash
# Draws the address and name drafts of the Tier 2 countries that are selected by host TLD (FR with its overseas departments, MC, BE, NL, LU,
# DE, AT, CH, LI) and writes the rules' pre-labels next to them.
#
# usage: tools/Eval/crawl/tier2-draw.sh <work dir> <rules dll> [<crawl id>]
#   <work dir> is a private directory (the pages and drafts hold personal data). Its exclude-hosts.txt lists hosts used by earlier sets.
#   Steps: select pages by TLD from random index blocks (seed 31), fetch them by WARC byte range (at most 4 threads, with back-off),
#   draw 120 address and 60 person + 50 company candidates per country, pre-label with the rules (country as hint for addresses).
# Adjudication then uses tools/Eval/tier2-south/corr.py and gold.sh, freezing uses tools/Eval/freeze-sets.py and scoring tools/Eval/tier2-south/run.sh.
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
work=${1:?work dir}
dll=${2:?rules dll}
crawl=${3:-CC-MAIN-2026-43}
cd "$work"
mkdir -p ctx/drafts
EXCLUDE_HOSTS=exclude-hosts.txt python3 "$here/select-pages-t2.py" "$crawl" . 31 fr=45 de=45 nl=40 be=30 at=30 ch=40 lu=100 li=0 mc=0 gp=0 mq=0 gf=0 re=0 yt=0
python3 "$here/fetch-pages.py" . 1000000 4
for c in FR DE NL BE AT CH LU LI MC; do
  python3 "$here/extract-candidates-t2.py" . 31 $c 120 60 50 | sed -n '1p'
  mv "draft-address-$c.jsonl" "draft-name-$c.jsonl" ctx/drafts/
  dotnet "$dll" address "ctx/drafts/draft-address-$c.jsonl" "ctx/drafts/pred-address-$c.jsonl" hint
  dotnet "$dll" name "ctx/drafts/draft-name-$c.jsonl" "ctx/drafts/pred-name-$c.jsonl"
done
