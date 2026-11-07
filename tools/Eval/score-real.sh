#!/usr/bin/env bash
# usage: score-real.sh <tag> <output directory>
# Builds the rules tool and the model tool, then runs score-all.sh over every evaluation set (rules + the Tier 1 + Tier 2 model, hinted and without a hint).
# Needs ONECONTACT_MODELS_MODEL (the saved pipeline). Run it again after a cut: countries whose rows are written are skipped.
set -uo pipefail
repo=$(cd "$(dirname "$0")/../.." && pwd)
cd "$repo"
export DOTNET_PROCESSOR_COUNT=4 DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
: "${ONECONTACT_MODELS_MODEL:?saved pipeline}"
for p in Rules Models; do
  nice dotnet build "tools/Eval/$p" -c Release -m:4 -v quiet -clp:NoSummary 2>&1 | grep -E 'error|warn' | sort -u | head
done
export DOTNET=dotnet
export RULES_DLL=$(find artifacts/bin/OneContact.Eval.Rules/release -name 'RecursiveAI.OneContact.Eval.Rules.dll' | head -1)
export MODELS_DLL=$(find artifacts/bin/OneContact.Eval.Models/release -name 'RecursiveAI.OneContact.Eval.Models.dll' | head -1)
RULES_DLL=$(realpath "$RULES_DLL") MODELS_DLL=$(realpath "$MODELS_DLL") exec bash tools/Eval/score-all.sh "$@"
