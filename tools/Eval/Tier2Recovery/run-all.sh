#!/usr/bin/env bash
# usage: run-all.sh <tag>; runs the recovery tool over the dev files of all 38 Tier 2 countries and writes <tag>/<cc>.txt and <tag>/misses-<cc>.tsv
# under ONECONTACT_TIER2_OUT (default ~/onecontact/tier2-recovery). The dev files are the register renders of each country's training data.
export PATH=~/dotnet10:$PATH DOTNET_ROOT=~/dotnet10 DOTNET_PROCESSOR_COUNT=4 DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
test -e ~/WORKER-BENCH.lock && { echo bench lock; exit 9; }
tag=${1:?tag}
out=${ONECONTACT_TIER2_OUT:-$HOME/onecontact/tier2-recovery}/$tag
mkdir -p "$out"
nice dotnet build tools/Eval/Tier2Recovery -c Release -m:4 -v quiet -clp:NoSummary > "$out/build.log" 2>&1
if grep -qE 'error|warning' "$out/build.log"; then grep -E 'error|warning' "$out/build.log" | sort -u | head; exit 1; fi
rm -f "$out"/*.txt "$out"/*.tsv*
exe=artifacts/bin/OneContact.Eval.Tier2Recovery/release/RecursiveAI.OneContact.Eval.Tier2Recovery
run() { # dir countries
  for cc in ${2//,/ }; do
    nice $exe "$HOME/onecontact/$1/out" 2000 "$cc" "$out/misses-$cc.tsv" > "$out/$cc.txt" 2>&1
  done
}
run u20 AT,BE,CH,DE,FR,LI,LU,MC,NL
run u21 AD,ES,GB,GG,IE,IM,IT,JE,MT,PT,SM,VA
run u24 CZ,EE,LV,NO
run u28 LT,MX,PL,SI,SK
run u29 DK,FI,IS,SE
run u30 HR,HU,RO
run u30b AU
grep -h 'hint=' "$out"/*.txt | cut -c1-400 > "$out/summary.txt"
wc -l "$out/summary.txt"
