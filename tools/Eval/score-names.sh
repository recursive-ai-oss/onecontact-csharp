#!/usr/bin/env bash
# Scores the name rows of two Tier 1 + Tier 2 models (a shipped one and a candidate) next to the rules alone and the stored reference predictions, for every
# country that has an evaluation set. Writes aggregate JSON only, to <output>/<country>-{rules,probablepeople,nameparser}.json (paired bootstrap against that
# reference) and prints nothing about any item.
# usage: tools/Eval/score-names.sh <output directory>
# Needs OLD_MODEL and NEW_MODEL (saved pipelines), MODELS_DLL (tools/Eval/Models build), RULES_DLL (tools/Eval/Rules build) and the sets under
# $ONECONTACT_EVAL (default ~/onecontact/eval/private). Predictions go to <set directory>/pred-<country>-names-<tag>/.
set -euo pipefail
repo=$(cd "$(dirname "$0")/../.." && pwd)
out=${1:?output directory}
: "${OLD_MODEL:?}" "${NEW_MODEL:?}" "${MODELS_DLL:?}" "${RULES_DLL:?}"
dn=${DOTNET:-dotnet}
root=${ONECONTACT_EVAL:-$HOME/onecontact/eval/private}
mkdir -p "$out"
each() { # set directory, countries
  for cc in $2; do
    if [ -n "${ONLY:-}" ] && [[ " $ONLY " != *" $cc "* ]]; then continue; fi
    local dir=$1 gn=$1/gold-name-$cc.jsonl base=$1/pred-$cc p=$1/pred-$cc-names
    mkdir -p "$p"
    "$dn" "$RULES_DLL" name "$gn" "$p/rules.jsonl" > /dev/null
    ONECONTACT_MODELS_MODEL=$OLD_MODEL "$dn" "$MODELS_DLL" name "$gn" "$p/old.jsonl" > /dev/null
    ONECONTACT_MODELS_MODEL=$NEW_MODEL "$dn" "$MODELS_DLL" name "$gn" "$p/new.jsonl" > /dev/null
    for ref in rules old probablepeople nameparser; do
      python3 "$repo/tools/Eval/score.py" name --gold "$gn" --pred rules="$p/rules.jsonl" --pred old="$p/old.jsonl" --pred new="$p/new.jsonl" \
        --pred probablepeople="$base/names/probablepeople.jsonl" --pred nameparser="$base/names/nameparser.jsonl" --reference $ref --json "$out/$cc-$ref.json" > /dev/null
    done
    if [ -f "$base/names/cleanco.jsonl" ]; then
      python3 "$repo/tools/Eval/tier2-south/legalform.py" --gold "$gn" --pred rules="$p/rules.jsonl" --pred old="$p/old.jsonl" --pred new="$p/new.jsonl" \
        --pred cleanco="$base/names/cleanco.jsonl" --reference cleanco > "$out/$cc-legalform.txt"
    fi
    echo "scored $cc"
  done
}
each "$root/tier2/u31/ctx/sets" "FR DE NL BE AT CH LU LI MC"
each "$root/tier2/u32/sets" "GB JE GG IM IE ES PT IT MT SM VA AD"
each "$root/tier2/u33/sets" "DK SE FI NO IS EE LV LT"
each "$root/tier2/u34/sets" "PL CZ SK SI HR HU RO MX AU"
each "$root/sets" "US CA"
