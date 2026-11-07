#!/usr/bin/env bash
# Baseline scoring of one fresh evaluation set: the reference systems already installed, the rules, and the rules with the shipped
# Tier 1 + Tier 2 model. Prints the scorer's aggregate rows only (counts, scores, paired bootstrap intervals); predictions stay in <dir>/pred-<cc>/.
# usage: score-fresh.sh <set directory> address|name <country>
# The directory holds gold-address-<cc>.jsonl or gold-name-<cc>.jsonl. Needs ONECONTACT_MODELS_MODEL (saved pipeline), RULES_DLL and MODELS_DLL
# (builds of tools/Eval/Rules and tools/Eval/Models), and the oracle installs of tools/Oracles under $ONECONTACT_ORACLES.
set -euo pipefail
repo=$(cd "$(dirname "$0")/../../.." && pwd)
dir=${1:?set directory}; kind=${2:?address|name}; cc=${3:?country}
: "${ONECONTACT_MODELS_MODEL:?}" "${RULES_DLL:?}" "${MODELS_DLL:?}"
dn=${DOTNET:-dotnet}
oracles=${ONECONTACT_ORACLES:-$HOME/onecontact/oracles}
lp=$oracles/libpostal
p=$dir/pred-$cc
mkdir -p "$p/names"
if [ "$kind" = address ]; then
  g=$dir/gold-address-$cc.jsonl
  "$dn" "$RULES_DLL" address "$g" "$p/rules-address.jsonl" > /dev/null
  "$dn" "$RULES_DLL" address "$g" "$p/rules-hint-address.jsonl" hint > /dev/null
  "$dn" "$MODELS_DLL" address "$g" "$p/model-address.jsonl" > /dev/null
  "$dn" "$MODELS_DLL" address "$g" "$p/model-hint-address.jsonl" hint > /dev/null
  PATH=$lp/mamba/envs/lp/bin:$PATH python "$repo/tools/Oracles/LibPostal/run-libpostal.py" "$g" "$p/libpostal-address.jsonl" "$lp/data-base" > /dev/null
  PATH=$lp/mamba/envs/lp/bin:$PATH python "$repo/tools/Oracles/LibPostal/run-libpostal.py" "$g" "$p/senzing-address.jsonl" "$lp/data-senzing" > /dev/null
  # WITH_COUNTRY=1 also runs both parsers given the item's country two ways: the documented `country` keyword (libpostal-country / senzing-country; it
  # changes nothing in the output) and the country name appended to the text (libpostal-name / senzing-name); see run-libpostal.py.
  extra=()
  if [ "${WITH_COUNTRY:-}" = 1 ]; then
    PATH=$lp/mamba/envs/lp/bin:$PATH python "$repo/tools/Oracles/LibPostal/run-libpostal.py" "$g" "$p/libpostal-country-address.jsonl" "$lp/data-base" country > /dev/null
    PATH=$lp/mamba/envs/lp/bin:$PATH python "$repo/tools/Oracles/LibPostal/run-libpostal.py" "$g" "$p/senzing-country-address.jsonl" "$lp/data-senzing" country > /dev/null
    PATH=$lp/mamba/envs/lp/bin:$PATH python "$repo/tools/Oracles/LibPostal/run-libpostal.py" "$g" "$p/libpostal-name-address.jsonl" "$lp/data-base" country-text > /dev/null
    PATH=$lp/mamba/envs/lp/bin:$PATH python "$repo/tools/Oracles/LibPostal/run-libpostal.py" "$g" "$p/senzing-name-address.jsonl" "$lp/data-senzing" country-text > /dev/null
    extra=(--pred libpostal-country="$p/libpostal-country-address.jsonl" --pred senzing-country="$p/senzing-country-address.jsonl"
           --pred libpostal-name="$p/libpostal-name-address.jsonl" --pred senzing-name="$p/senzing-name-address.jsonl")
  fi
  echo "== addresses $cc"
  python3 "$repo/tools/Eval/score.py" address --gold "$g" --pred rules="$p/rules-address.jsonl" --pred rules-hint="$p/rules-hint-address.jsonl" \
    --pred model="$p/model-address.jsonl" --pred model-hint="$p/model-hint-address.jsonl" \
    --pred libpostal="$p/libpostal-address.jsonl" --pred senzing="$p/senzing-address.jsonl" "${extra[@]}" --reference ${REFERENCE:-libpostal} --json "$p/scores-address.json"
else
  g=$dir/gold-name-$cc.jsonl
  "$dn" "$RULES_DLL" name "$g" "$p/rules-name.jsonl" > /dev/null
  "$dn" "$MODELS_DLL" name "$g" "$p/model-name.jsonl" > /dev/null
  "$oracles/names-venv/bin/python" -W ignore "$repo/tools/Oracles/Names/run-eval-names.py" "$g" "$p/names" > /dev/null
  for ref in probablepeople nameparser; do
    echo "== names $cc (reference $ref)"
    python3 "$repo/tools/Eval/score.py" name --gold "$g" --pred rules="$p/rules-name.jsonl" --pred model="$p/model-name.jsonl" \
      --pred probablepeople="$p/names/probablepeople.jsonl" --pred nameparser="$p/names/nameparser.jsonl" --reference $ref --json "$p/scores-name-$ref.json"
  done
  if [ -f "$p/names/cleanco.jsonl" ]; then
    echo "== legal forms $cc"
    python3 "$repo/tools/Eval/tier2-south/legalform.py" --gold "$g" --pred rules="$p/rules-name.jsonl" --pred model="$p/model-name.jsonl" \
      --pred probablepeople="$p/names/probablepeople.jsonl" --pred cleanco="$p/names/cleanco.jsonl" --reference cleanco
  fi
fi
