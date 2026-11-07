#!/usr/bin/env bash
# usage: tools/bench/peak-rss.sh <label>
# Runs every comparison row's benchmark methods one at a time in its own process and records the process's peak resident set, so
# each side of a row has its own peak. The figure includes the runtime and the harness; compare it with the "none" line. Results go to
# $ONECONTACT_WORK/bench/results/<label>/peak-rss.txt.
set -uf
cd "$(dirname "$0")/../.."
. tools/bench/env.sh
export DOTNET_PROCESSOR_COUNT=4
label=$1
OUT=${ONECONTACT_WORK:-$HOME/onecontact/work}/bench/results/$label
mkdir -p "$OUT"
DLL="${SNAP:-$ONECONTACT_ARTIFACTS/$(basename "$PWD")/bin/OneContact.Bench/release}/RecursiveAI.OneContact.Bench.dll"
if [ "$(uname)" = Darwin ]; then TIMEV="/usr/bin/time -l"; else TIMEV="/usr/bin/time -v"; fi
peak() { # bytes or KB from the time tool's report
  if [ "$(uname)" = Darwin ]; then awk '/maximum resident set size/ {printf "%.0f", $1/1048576}' "$1"; else awk -F: '/Maximum resident set size/ {printf "%.0f", $2/1024}' "$1"; fi
}
: > "$OUT/peak-rss.txt"
dotnet "$DLL" first-use none > /dev/null 2> "$OUT/t.tmp"
$TIMEV dotnet "$DLL" first-use none > /dev/null 2> "$OUT/t.tmp"
echo "| runtime baseline | $(peak "$OUT/t.tmp") MB |" >> "$OUT/peak-rss.txt"
dotnet "$DLL" --list flat --filter '*CompareRow*' 2>/dev/null | grep 'CompareRow\.' > "$OUT/methods.txt"
while read -r m; do
  $TIMEV dotnet "$DLL" --filter "$m" > /dev/null 2> "$OUT/t.tmp"
  echo "| $m | $(peak "$OUT/t.tmp") MB |" >> "$OUT/peak-rss.txt"
done < "$OUT/methods.txt"
rm -f "$OUT/t.tmp"
cat "$OUT/peak-rss.txt"
