#!/usr/bin/env bash
# Scores the accurate-tier model next to the shipped Tier 1 + Tier 2 model, the rules alone and the stored reference predictions (libpostal, Senzing,
# probablepeople, python-nameparser, cleanco), on the frozen evaluation sets, hinted and without a hint. Each model is run once per set and nothing is tuned.
# Aggregates only: the scorer prints counts, scores and paired bootstrap intervals; predictions stay in <set directory>/pred-<country>-<tag>/.
# usage: tools/Eval/score-accurate.sh <tag> <output directory>
# Needs SHIPPED_MODEL and ACCURATE_MODEL (saved pipelines), FAST_DLL (tools/Eval/Models build), ACCURATE_DLL (tools/Eval/ModelsAccurate build) and
# RULES_DLL (tools/Eval/Rules build), and the sets under $ONECONTACT_EVAL (default ~/onecontact/eval/private). A country whose output file holds its rows is skipped.
set -uo pipefail
repo=$(cd "$(dirname "$0")/../.." && pwd)
tag=${1:?tag}
out=${2:?output directory}
: "${SHIPPED_MODEL:?}" "${ACCURATE_MODEL:?}" "${FAST_DLL:?}" "${ACCURATE_DLL:?}" "${RULES_DLL:?}"
dn=${DOTNET:-dotnet}
root=${ONECONTACT_EVAL:-$HOME/onecontact/eval/private}
mkdir -p "$out"
fast() { ONECONTACT_MODELS_MODEL=$SHIPPED_MODEL "$dn" "$FAST_DLL" "$@" > /dev/null; }
acc() { ONECONTACT_ACCURATE_MODEL=$ACCURATE_MODEL "$dn" "$ACCURATE_DLL" "$@" > /dev/null; }

# set directory, country, kinds (address, name)
one() {
  local dir=$1 cc=$2 kinds=$3 base=$1/pred-$2 p=$1/pred-$2-$tag
  if grep -qs 'full-parse\|class' "$out/$cc.txt"; then return; fi
  mkdir -p "$p"
  {
    if [[ " $kinds " == *" address "* ]]; then
      local ga=$dir/gold-address-$cc.jsonl
      "$dn" "$RULES_DLL" address "$ga" "$p/rules-address.jsonl" > /dev/null
      "$dn" "$RULES_DLL" address "$ga" "$p/rules-hint-address.jsonl" hint > /dev/null
      fast address "$ga" "$p/shipped-address.jsonl"
      fast address "$ga" "$p/shipped-hint-address.jsonl" hint
      acc address "$ga" "$p/accurate-address.jsonl"
      acc address "$ga" "$p/accurate-hint-address.jsonl" hint
      echo "== addresses $cc"
      python3 "$repo/tools/Eval/score.py" address --gold "$ga" --pred shipped="$p/shipped-address.jsonl" --pred shipped-hint="$p/shipped-hint-address.jsonl" \
        --pred accurate="$p/accurate-address.jsonl" --pred accurate-hint="$p/accurate-hint-address.jsonl" \
        --pred rules="$p/rules-address.jsonl" --pred rules-hint="$p/rules-hint-address.jsonl" \
        --pred libpostal="$base/libpostal-address.jsonl" --pred senzing="$base/senzing-address.jsonl" --reference libpostal --json "$out/$cc-address.json"
    fi
    if [[ " $kinds " == *" name "* ]]; then
      local gn=$dir/gold-name-$cc.jsonl
      "$dn" "$RULES_DLL" name "$gn" "$p/rules-name.jsonl" > /dev/null
      fast name "$gn" "$p/shipped-name.jsonl"
      acc name "$gn" "$p/accurate-name.jsonl"
      echo "== names $cc"
      python3 "$repo/tools/Eval/score.py" name --gold "$gn" --pred shipped="$p/shipped-name.jsonl" --pred accurate="$p/accurate-name.jsonl" --pred rules="$p/rules-name.jsonl" \
        --pred probablepeople="$base/names/probablepeople.jsonl" --pred nameparser="$base/names/nameparser.jsonl" --reference probablepeople --json "$out/$cc-name.json"
      if [ -f "$base/names/cleanco.jsonl" ]; then
        echo "== legal forms $cc"
        python3 "$repo/tools/Eval/tier2-south/legalform.py" --gold "$gn" --pred shipped="$p/shipped-name.jsonl" --pred accurate="$p/accurate-name.jsonl" --pred rules="$p/rules-name.jsonl" \
          --pred cleanco="$base/names/cleanco.jsonl" --reference cleanco
      fi
    fi
  } > "$out/$cc.txt" 2>&1 || echo "failed: $cc"
}

each() { for cc in $3; do one "$1" "$cc" "$2"; done; }
each "$root/tier2/group-a/sets" "address name" "FR DE NL BE AT CH LU LI MC"
each "$root/tier2/group-b/sets" "address name" "GB JE GG IM IE ES PT IT MT SM VA AD"
each "$root/tier2/c-north/sets" "address name" "DK SE FI NO IS EE LV LT"
each "$root/tier2/c-south/sets" "address name" "PL CZ SK SI HR HU AU"
each "$root/sets" "address" "US CA"
each "$root/fresh/ro-mx" "address name" "RO MX"
# The United States and Canada name rows are the fresh sets; their output files are named <country>-names.txt so they do not collide with the address rows.
for cc in US CA; do
  out_names=$out
  tmp_out=$out/names-$cc
  mkdir -p "$tmp_out"
  ( out=$tmp_out; one "$root/fresh/names" "$cc" "name" )
done
