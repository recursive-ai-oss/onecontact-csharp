#!/usr/bin/env bash
# Adds the contact-span head to trained Tier 1 weights, for at most BUDGET seconds; run again with the same arguments to continue.
#   BASE       the trained Tier 1 pipeline (tier1.onenlp) to add the head to
#   BLOCKS     folder of rendered contact blocks (train/ and dev/ inside), see tools/data/provenance/blocks-us-ca.md
#   OUT        checkpoint and model folder
#   BUDGET     seconds for this chunk (default 1500); THREADS, SEED, EPOCHS, BLOCKS_COUNT as in recipes/tier1/Program.cs
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
dll="${RECIPE_DLL:-$here/../../artifacts/bin/OneContact.Recipe.Tier1/release/RecursiveAI.OneContact.Recipe.Tier1.dll}"
mkdir -p "$OUT"
exec dotnet "$dll" spans --base "$BASE" \
  --blocks "$BLOCKS/train" --blocks-dev "$BLOCKS/dev" \
  --blocks-count "${BLOCKS_COUNT:-500000}" --blocks-dev-count 6000 \
  --seed "${SEED:-1}" --epochs "${EPOCHS:-6}" --threads "${THREADS:-8}" --budget-seconds "${BUDGET:-1500}" \
  --checkpoint-every 500 --out "$OUT"
