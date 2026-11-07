#!/usr/bin/env bash
# Quick local run of the group C-3 countries (Hungary, Australia, Romania): count, render into a scratch directory, check, look at samples.
set -euo pipefail
export DOTNET_PROCESSOR_COUNT=4
here=$(cd "$(dirname "$0")" && pwd)
root=$(cd "$here/../.." && pwd)
(cd "$here/AddressTier2C" && nice dotnet build -c Release -m:4 -v quiet -clp:NoSummary 2>&1 | tail -5)
dll=$root/artifacts/bin/OneContact.Data.AddressTier2C/release/RecursiveAI.OneContact.Data.AddressTier2C.dll
out=${1:-${ONECONTACT_WORK:-$HOME/onecontact/work}/tier2-east/out}
try=${2:-${ONECONTACT_WORK:-$HOME/onecontact/work}/tier2-east/try}
countries=${3:-HU,AU,RO}
data=${ONECONTACT_DATA:-$HOME/onecontact/data}/train
mkdir -p "$out" "$try"
nice dotnet "$dll" count --data "$data" --out "$out" --countries "$countries" | tail -5
cat "$out"/rejects-count-*.tsv
nice dotnet "$dll" render --data "$data" --out "$try" --selection "$out/selection.tsv" --countries "$countries" | tail -8
nice dotnet "$dll" check --out "$try" | tail -8
for c in ${countries//,/ }; do
  echo "== $c"
  python3 "$here/sample-examples.py" "$try/$(echo "$c" | tr A-Z a-z).train.jsonl" 6
done
