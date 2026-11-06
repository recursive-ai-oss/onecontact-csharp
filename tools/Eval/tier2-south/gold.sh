#!/usr/bin/env bash
# Builds the gold files of one country from its drafts, pre-labels and corrections (see corr.py), and prints the label provenance counts.
# usage: gold.sh <set root> <country> [labeler]
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
root=${1:?set root}; cc=${2:?country}; who=${3:-agent adjudication}
d=$root/drafts; out=$root/sets; mkdir -p "$out"
touch "$d/corr-address-$cc.jsonl" "$d/corr-name-$cc.jsonl"
python3 "$here/../labeling.py" apply address "$d/draft-address-$cc.jsonl" "$d/pred-address-$cc.jsonl" "$d/corr-address-$cc.jsonl" "$out/gold-address-$cc.jsonl" "$who"
python3 "$here/../labeling.py" apply name "$d/draft-name-$cc.jsonl" "$d/pred-name-$cc.jsonl" "$d/corr-name-$cc.jsonl" "$out/gold-name-$cc.jsonl" "$who"
