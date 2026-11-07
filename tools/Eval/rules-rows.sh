#!/usr/bin/env bash
# Scores the rules alone (names and addresses, hinted and without a hint) on the Tier 2 south sets next to the saved reference predictions.
# Writes the scorer's aggregate output to <output>/score-<cc>-{name,addr-hint,addr-nohint}.txt and the name confusion counts to <output>/confusion-<cc>.txt.
# usage: tools/Eval/rules-rows.sh <output directory> <country>...
# Needs the Rules tool built (RULES_DLL) and the sets under $ONECONTACT_EVAL (default ~/onecontact/eval/private). Prints nothing about any item.
set -euo pipefail
repo=$(cd "$(dirname "$0")/../.." && pwd)
out=${1:?output directory}
shift
: "${RULES_DLL:?}"
dn=${DOTNET:-dotnet}
root=${ONECONTACT_EVAL:-$HOME/onecontact/eval/private}/tier2/u34/sets
mkdir -p "$out"
for cc in "$@"; do
  base=$root/pred-$cc
  tmp=$out/pred-$cc
  mkdir -p "$tmp"
  "$dn" "$RULES_DLL" name "$root/gold-name-$cc.jsonl" "$tmp/name.jsonl" > /dev/null
  "$dn" "$RULES_DLL" address "$root/gold-address-$cc.jsonl" "$tmp/addr-nohint.jsonl" > /dev/null
  "$dn" "$RULES_DLL" address "$root/gold-address-$cc.jsonl" "$tmp/addr-hint.jsonl" hint > /dev/null
  python3 "$repo/tools/Eval/score.py" name --gold "$root/gold-name-$cc.jsonl" --pred rules="$tmp/name.jsonl" \
    --pred probablepeople="$base/names/probablepeople.jsonl" --pred nameparser="$base/names/nameparser.jsonl" --reference nameparser > "$out/score-$cc-name.txt"
  python3 "$repo/tools/Eval/name-confusion.py" --gold "$root/gold-name-$cc.jsonl" --pred rules="$tmp/name.jsonl" \
    --pred nameparser="$base/names/nameparser.jsonl" > "$out/confusion-$cc.txt"
  for mode in hint nohint; do
    python3 "$repo/tools/Eval/score.py" address --gold "$root/gold-address-$cc.jsonl" --pred rules="$tmp/addr-$mode.jsonl" \
      --pred libpostal="$base/libpostal-address.jsonl" --pred senzing="$base/senzing-address.jsonl" --reference libpostal > "$out/score-$cc-addr-$mode.txt"
  done
  echo "scored $cc"
done
