#!/usr/bin/env bash
# Converts the OpenStreetMap address file of the published libpostal parser training set into challenger training examples.
#
#   usage: run-challenger.sh <AddressChallenger dll> <step>...      steps: count render check verify
#
#   count   one pass over the file: lines and usable lines per country, and each country's sampling rate (selection.tsv)
#   render  one pass: examples of the selected lines, train and dev files per country, manifest.json
#   check   span rules and the split rule on every written line (0 failures expected)
#   verify  renders a second time into a sibling folder and compares every byte
#
# Input  $ONECONTACT_DATA/challenger/osm/formatted_addresses_tagged.random.tsv.gz   (tools/data/fetch-challenger.sh)
# Output $ONECONTACT_DATA/challenger/train/osm-lp-v1/   (challenger lineage; the default models' recipes never read this folder)
# Each step is one single-threaded pass of about ten minutes.
set -euo pipefail

dll=${1:?usage: run-challenger.sh <AddressChallenger dll> <step>...}
shift
root=${ONECONTACT_DATA:-$HOME/onecontact/data}/challenger
input=$root/osm/formatted_addresses_tagged.random.tsv.gz
out=$root/train/osm-lp-v1
seed=${SEED:-20261106}
mkdir -p "$out"

for step in "$@"; do
  case $step in
    count)  dotnet "$dll" count --input "$input" --out "$out" --target "${TARGET:-250000}" ;;
    render) dotnet "$dll" render --input "$input" --out "$out" --seed "$seed" ;;
    check)  dotnet "$dll" check --out "$out" ;;
    verify)
      second=$out-verify
      rm -rf "$second"; mkdir -p "$second"
      cp "$out/selection.tsv" "$second/selection.tsv"
      dotnet "$dll" render --input "$input" --out "$second" --seed "$seed"
      for f in "$out"/*.jsonl "$out"/manifest.json "$out"/rejects-render.tsv; do
        cmp "$f" "$second/$(basename "$f")" || { echo "DIFFERENT $(basename "$f")" >&2; exit 1; }
      done
      echo "second render identical in all $(ls "$out"/*.jsonl | wc -l) data files, the manifest and the reject counts"
      rm -rf "$second" ;;
    *) echo "unknown step $step" >&2; exit 2 ;;
  esac
done
