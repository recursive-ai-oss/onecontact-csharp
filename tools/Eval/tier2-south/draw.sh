#!/usr/bin/env bash
# Draws the address and name drafts of the Tier 2 south countries from their stored pages and writes the rules' pre-labels next to them.
# usage: draw.sh <set root> <rules dll> [<addresses> <persons> <companies>]    (set root holds <CC>/pages.jsonl; drafts go to <set root>/drafts)
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
root=${1:?set root}
dll=${2:?rules dll}
na=${3:-130}; np=${4:-70}; nc=${5:-60}
mkdir -p "$root/drafts"
for c in ${COUNTRIES:-PL CZ SK SI HR HU RO MX AU}; do
  python3 "$here/extract-candidates.py" $c "$root/$c/pages.jsonl" "$root/drafts" 1 "$na" "$np" "$nc" | sed -n '1p'
  dotnet "$dll" address "$root/drafts/draft-address-$c.jsonl" "$root/drafts/pred-address-$c.jsonl" hint
  dotnet "$dll" name "$root/drafts/draft-name-$c.jsonl" "$root/drafts/pred-name-$c.jsonl"
done
