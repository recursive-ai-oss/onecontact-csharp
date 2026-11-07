#!/usr/bin/env bash
# usage: run-all.sh <tag>; runs the recovery tool over the dev files of all 38 Tier 2 countries and writes <tag>/<cc>.txt and <tag>/misses-<cc>.tsv
# under ONECONTACT_TIER2_OUT (default ~/onecontact/tier2-recovery). Rendered sets are read from $ONECONTACT_DATA/rendered/<group> (default ~/onecontact/data/rendered). The dev files are the register renders of each country's training data.
export DOTNET_PROCESSOR_COUNT=4 DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
tag=${1:?tag}
out=${ONECONTACT_TIER2_OUT:-$HOME/onecontact/tier2-recovery}/$tag
mkdir -p "$out"
nice dotnet build tools/Eval/Tier2Recovery -c Release -m:4 -v quiet -clp:NoSummary > "$out/build.log" 2>&1
if grep -qE 'error|warning' "$out/build.log"; then grep -E 'error|warning' "$out/build.log" | sort -u | head; exit 1; fi
rm -f "$out"/*.txt "$out"/*.tsv*
exe=artifacts/bin/OneContact.Eval.Tier2Recovery/release/RecursiveAI.OneContact.Eval.Tier2Recovery
run() { # dir countries
  for cc in ${2//,/ }; do
    nice $exe "${ONECONTACT_DATA:-$HOME/onecontact/data}/rendered/$1" 2000 "$cc" "$out/misses-$cc.tsv" > "$out/$cc.txt" 2>&1
  done
}
run tier2-a AT,BE,CH,DE,FR,LI,LU,MC,NL
run tier2-b AD,ES,GB,GG,IE,IM,IT,JE,MT,PT,SM,VA
run tier2-c-north CZ,EE,LV,NO
run tier2-c-south LT,MX,PL,SI,SK
run tier2-c-nordic DK,FI,IS,SE
run tier2-c-east HR,HU,RO
run tier2-au AU
grep -h 'hint=' "$out"/*.txt | cut -c1-400 > "$out/summary.txt"
wc -l "$out/summary.txt"
