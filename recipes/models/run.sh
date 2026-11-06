#!/usr/bin/env bash
# Trains (or resumes) the Tier 1 + Tier 2 model for at most BUDGET seconds. Run it again with the same arguments to continue from the last
# checkpoint; the result equals one uninterrupted run.
#   ADDRESSES         folder with <cc>.train.jsonl and <cc>.dev.jsonl for the United States (us), Canada (ca), the US territories (ust) and every Tier 2
#                     country, as rendered by the tools in tools/data (see README.md)
#   NAMES_US_CA       folder with train/ and dev/ of the United States and Canada name set (tools/data/names)
#   NAMES_TIER2       folder with train/ and dev/ of the Tier 2 name set (tools/data/names-tier2)
#   OUT               checkpoint and model folder
#   BUDGET            seconds for this chunk (default 1500)
#   THREADS, SEED, EPOCHS, ADDRESS_COUNT, BIG_COUNT, NAMES_COUNT, NAMES2_COUNT   see recipes/models/Program.cs
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
A="${ADDRESSES:?set ADDRESSES}"
OUT="${OUT:?set OUT}"
mkdir -p "$OUT"
dll="${RECIPE_DLL:-$here/../../artifacts/bin/OneContact.Recipe.Models/release/RecursiveAI.OneContact.Recipe.Models.dll}"
train=""; dev=""
for f in "$A"/*.train.jsonl; do train="$train,$f"; done
for f in "$A"/*.dev.jsonl; do dev="$dev,$f"; done
exec dotnet "$dll" train \
  --addresses "${train#,}" --addresses-dev "${dev#,}" --address-big us,ca,ust \
  --address-count "${ADDRESS_COUNT:-925000}" --address-big-count "${BIG_COUNT:-80000}" \
  --address-dev-count 14000 --address-big-dev-count 1500 \
  --names "${NAMES_US_CA:?set NAMES_US_CA}/train" --names-dev "$NAMES_US_CA/dev" --names-count "${NAMES_COUNT:-250000}" --names-dev-count 4000 \
  --names2 "${NAMES_TIER2:?set NAMES_TIER2}/train" --names2-dev "$NAMES_TIER2/dev" --names2-count "${NAMES2_COUNT:-250000}" --names2-dev-count 4000 \
  --seed "${SEED:-1}" --epochs "${EPOCHS:-6}" --threads "${THREADS:-8}" --budget-seconds "${BUDGET:-1500}" \
  --checkpoint-every 500 --out "$OUT"
