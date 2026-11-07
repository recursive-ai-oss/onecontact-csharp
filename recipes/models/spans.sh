#!/usr/bin/env bash
# Adds the contact-span head to the trained pipeline, for at most BUDGET seconds; run again with the same arguments to continue.
#   BASE        the trained pipeline (models.onenlp from run.sh)
#   BLOCKS_US_CA  folder with train/ and dev/ of the United States and Canada blocks (tools/data/blocks, render)
#   BLOCKS_TIER2  folder with train/ and dev/ of the Tier 2 blocks (recipes/models/blocks-tier2.sh)
#   OUT         checkpoint and model folder; BUDGET, THREADS, SEED, EPOCHS, BLOCKS_COUNT as in recipes/models/Program.cs
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
dll="${RECIPE_DLL:-$here/../../artifacts/bin/OneContact.Recipe.Models/release/RecursiveAI.OneContact.Recipe.Models.dll}"
mkdir -p "$OUT"
exec dotnet "$dll" spans --base "$BASE" \
  --blocks "$BLOCKS_US_CA/train,$BLOCKS_TIER2/train" --blocks-dev "$BLOCKS_US_CA/dev,$BLOCKS_TIER2/dev" \
  --blocks-count "${BLOCKS_COUNT:-500000}" --blocks-dev-count 8000 \
  --seed "${SEED:-1}" --epochs "${EPOCHS:-4}" --threads "${THREADS:-8}" --budget-seconds "${BUDGET:-1500}" \
  --checkpoint-every 500 --out "$OUT"
