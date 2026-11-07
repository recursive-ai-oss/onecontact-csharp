#!/usr/bin/env bash
# Scores the free-text name rules on synthetic CRM-style texts and on the contact-block development set. Rules only unless MODEL=1.
# usage: tools/Eval/extraction/crm-eval.sh [count per category]       (run from the repository root on a build machine)
# Environment: ONECONTACT_TIER1_MODEL (a saved Tier 1 pipeline), NAMES_ROOT (the name training sets), BLOCKS (the block development file).
set -uo pipefail
cd "$(dirname "$0")/../../.."
. tools/bench/env.sh
export DOTNET_PROCESSOR_COUNT=4 MSBUILDDISABLENODEREUSE=1
count=${1:-300}
names=${NAMES_ROOT:-$HOME/onecontact/data/train}
blocks=${BLOCKS:-$HOME/onecontact/data/train/blocks-us-ca-v3/dev/part-00000.jsonl}
out=${ONECONTACT_ARTIFACTS:-$HOME/onecontact/artifacts}/crm-texts
rm -rf "$out"
python3 tools/Eval/extraction/crm-texts.py "$names" "$out" "$count"
nice dotnet build -c Release -m:4 -v quiet -clp:NoSummary tools/Eval/Extraction/OneContact.Eval.Extraction.csproj 2>&1 | grep -E ' error |warning' | sort -u | cut -c1-240 | head
dll=$(find "$ONECONTACT_ARTIFACTS" -path '*bin/OneContact.Eval.Extraction/release/RecursiveAI.OneContact.Eval.Extraction.dll' | head -1)
[ -z "${MODEL:-}" ] && export RULES_ONLY=1
for f in "$out"/crm-*.jsonl; do
  printf '%-34s' "$(basename "$f" .jsonl)"
  dotnet "$dll" dev "$ONECONTACT_TIER1_MODEL" "$f" 100000 | grep -E '^person ' | head -1 | sed -E 's/rules only: //' | cut -c1-150
done
blk=$(dotnet "$dll" dev "$ONECONTACT_TIER1_MODEL" "$blocks" 5000 | grep -E '^(person|company) ' | grep 'rules only')
echo "$blk" | while read -r l; do printf '%-34s%s\n' "blocks-v3-dev (5000)" "$(echo "$l" | sed -E 's/rules only: //' | cut -c1-150)"; done
