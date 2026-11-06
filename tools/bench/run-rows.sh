#!/usr/bin/env bash
# usage: run-rows.sh <label> <BenchmarkDotNet filter> [extra args]
# Runs the bench rows named by the filter in one process and writes the BenchmarkDotNet tables and a load record (CPU, cores and load before,
# during and after) to $OUT. Timings taken this way are indicative: the machine is shared.
set -u
cd "$(dirname "$0")/../.."
. tools/bench/env.sh
export DOTNET_PROCESSOR_COUNT=4
label=$1; filter=$2; shift 2
OUT=$HOME/onecontact/u37/results/$label
mkdir -p "$OUT"
{ echo "cpu: $(grep -m1 'model name' /proc/cpuinfo | cut -d: -f2)"; echo "cores: $(nproc)"; echo "before: $(cat /proc/loadavg)"; } > "$OUT/load.txt"
( while sleep 20; do echo "during: $(cat /proc/loadavg)" >> "$OUT/load.txt"; done ) &
sampler=$!
nice -n 5 dotnet "${SNAP:-$ONECONTACT_ARTIFACTS/default/bin/OneContact.Bench/release}/RecursiveAI.OneContact.Bench.dll" --filter "$filter" --artifacts "$OUT/bdn" "$@" > "$OUT/log.txt" 2>&1
status=$?
kill $sampler 2>/dev/null
echo "after: $(cat /proc/loadavg)" >> "$OUT/load.txt"
grep -hE '^\| |^Mean|BenchmarkDotNet v|^\.NET|^Job|OS=|AMD|Intel' "$OUT/log.txt" | head -80
cat "$OUT/load.txt"
exit $status
