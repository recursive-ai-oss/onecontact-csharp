#!/usr/bin/env bash
# Trains (or resumes) the Tier 1 model for at most BUDGET seconds, from the rendered training sets under $ONECONTACT_DATA.
# Run it again with the same arguments to continue from the last checkpoint; the result equals one uninterrupted run.
#   ONECONTACT_DATA   folder holding u-address/ and names sets (default ~/onecontact)
#   OUT               checkpoint and model folder (default $ONECONTACT_DATA/runs/tier1)
#   BUDGET            seconds for this chunk (default 1500)
#   THREADS, SEED, EPOCHS, ADDRESS_COUNT, NAMES_COUNT   see recipes/tier1/Program.cs
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
D="${ONECONTACT_DATA:-$HOME/onecontact}"
OUT="${OUT:-$D/runs/tier1}"
mkdir -p "$OUT"
dll="${RECIPE_DLL:-$here/../../artifacts/bin/OneContact.Recipe.Tier1/release/RecursiveAI.OneContact.Recipe.Tier1.dll}"
exec dotnet "$dll" train \
  --addresses "$D/addresses/us.train.jsonl,$D/addresses/ca.train.jsonl,$D/addresses/us-territories.train.jsonl" \
  --addresses-dev "$D/addresses/us.dev.jsonl,$D/addresses/ca.dev.jsonl,$D/addresses/us-territories.dev.jsonl" \
  --names "$D/names/train" --names-dev "$D/names/dev" \
  --address-count "${ADDRESS_COUNT:-600000}" --names-count "${NAMES_COUNT:-300000}" \
  --address-dev-count 9000 --names-dev-count 9000 \
  --seed "${SEED:-1}" --epochs "${EPOCHS:-10}" --threads "${THREADS:-8}" --budget-seconds "${BUDGET:-1500}" \
  --checkpoint-every 500 --out "$OUT"
