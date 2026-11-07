#!/usr/bin/env bash
# usage: run.sh [countries]; ONECONTACT_TIER2B_DATA is the directory of the dev files, ONECONTACT_TIER2B_OUT receives misses.tsv
# Builds the recovery tool and runs it over the group B dev files .
export DOTNET_PROCESSOR_COUNT=4 DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
nice dotnet build tools/Eval/Tier2Recovery -c Release -m:4 -v quiet -clp:NoSummary 2>&1 | grep -E 'error|warn' | sort -u | head
mkdir -p ${ONECONTACT_TIER2B_OUT:-$HOME/tier2b-recovery}
nice artifacts/bin/OneContact.Eval.Tier2Recovery/release/RecursiveAI.OneContact.Eval.Tier2Recovery ${ONECONTACT_TIER2B_DATA:-${ONECONTACT_DATA:-$HOME/onecontact/data}/rendered/tier2-b} 2000 "${1:-GB,JE,GG,IM,IE,ES,PT,IT,SM,VA,MT,AD}" ${ONECONTACT_TIER2B_OUT:-$HOME/tier2b-recovery}/misses.tsv 2>&1 | cut -c1-900
