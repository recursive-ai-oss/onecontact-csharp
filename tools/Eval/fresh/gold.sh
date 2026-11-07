#!/usr/bin/env bash
# Builds the gold file of one country's fresh address set from the first N drafts (the ones a reviewer read), their rules pre-labels and the
# corrections file, and prints the label provenance counts. Drafts after N were never read and are left out.
# usage: gold.sh <set root> <CC> <N> [labeler]
# Corrections are written with tools/Eval/tier2-south/corr.py (spec lines `addr <number> drop`, `addr <number> key=value|key=value`, `=` first
# to replace the pre-label instead of editing it). Writes <set root>/sets/gold-address-<CC>.jsonl; freeze-sets.py fixes it afterwards.
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
root=${1:?set root}; cc=${2:?country}; n=${3:?count}; who=${4:-adjudicated}
d=$root/drafts; mkdir -p "$root/sets"
touch "$d/corr-address-$cc.jsonl"
head -n "$n" "$d/draft-address-$cc.jsonl" > "$d/read-draft-address-$cc.jsonl"
python3 "$here/../labeling.py" apply address "$d/read-draft-address-$cc.jsonl" "$d/pred-address-$cc.jsonl" "$d/corr-address-$cc.jsonl" "$root/sets/gold-address-$cc.jsonl" "$who"
