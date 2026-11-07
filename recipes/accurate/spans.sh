#!/usr/bin/env bash
# Adds the contact-span head to the trained heads, for at most BUDGET seconds; run again with the same arguments to continue.
#   BASE          the trained heads (heads.onenlp from run.sh)
#   BLOCKS_US_CA, BLOCKS_TIER2   folders with train/ and dev/ of the contact blocks (tools/data/blocks)
#   OUT           checkpoint and model folder (the result is OUT/accurate.onenlp); BUDGET, THREADS, SEED, EPOCHS, BLOCKS_COUNT as in Program.cs
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
dll="${RECIPE_DLL:-$here/../../artifacts/bin/OneContact.Recipe.Accurate/release/RecursiveAI.OneContact.Recipe.Accurate.dll}"
mkdir -p "${OUT:?set OUT}"
exec dotnet "$dll" spans --base "${BASE:?set BASE}" \
  --blocks "${BLOCKS_US_CA:?set BLOCKS_US_CA}/train,${BLOCKS_TIER2:?set BLOCKS_TIER2}/train" --blocks-dev "$BLOCKS_US_CA/dev,$BLOCKS_TIER2/dev" \
  --blocks-count "${BLOCKS_COUNT:-100000}" --blocks-dev-count 4000 \
  --seed "${SEED:-1}" --epochs "${EPOCHS:-3}" --threads "${THREADS:-8}" --budget-seconds "${BUDGET:-1500}" \
  --checkpoint-every 500 --out "$OUT"
