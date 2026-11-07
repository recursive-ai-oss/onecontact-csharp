#!/usr/bin/env bash
# usage: score-all.sh <tag> <output directory>
# Scores the current rules with the Tier 1 + Tier 2 model on every evaluation set (hinted and without a hint) and writes the scorer's aggregate rows to <output>/<country>.txt.
# Needs ONECONTACT_MODELS_MODEL (the saved pipeline), MODELS_DLL and RULES_DLL (see run-superset.sh) and the evaluation sets under $ONECONTACT_EVAL (default ~/onecontact/eval/private).
# A country whose output file already holds its rows is skipped, so a run cut short resumes where it stopped.
set -uo pipefail
repo=$(cd "$(dirname "$0")/../.." && pwd)
tag=${1:?tag}
out=${2:?output directory}
eval_root=${ONECONTACT_EVAL:-$HOME/onecontact/eval/private}
mkdir -p "$out"
run() { # set directory, countries
  for cc in $2; do
    if grep -qs 'full-parse' "$out/$cc.txt"; then continue; fi
    SUPERSET_TAG=$tag bash "$repo/tools/Eval/run-superset.sh" "$1" "$cc" > "$out/$cc.txt" 2>&1 || echo "failed: $cc"
  done
}
run "$eval_root/tier2/group-a/sets" "FR DE NL BE AT CH LU LI MC"
run "$eval_root/tier2/group-b/sets" "GB JE GG IM IE ES PT IT MT SM VA AD"
run "$eval_root/tier2/c-north/sets" "DK SE FI NO IS EE LV LT"
run "$eval_root/tier2/c-south/sets" "PL CZ SK SI HR HU RO MX AU"
run "$eval_root/sets" "US CA"
