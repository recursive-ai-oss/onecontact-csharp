#!/usr/bin/env bash
# Trains (or resumes) an address challenger for at most BUDGET seconds: the Tier 1 + Tier 2 pipeline shape trained on the converted OpenStreetMap address
# sets, alone (candidate C1) or mixed with the register-rendered sets (candidate C2). Run it again with the same arguments to continue from the last
# checkpoint; the result equals one uninterrupted run. Only the address head gets examples; the name heads stay untrained and unused.
#   OSM               folder with <cc>.train.jsonl and <cc>.dev.jsonl made by tools/data/AddressChallenger (ODbL data: the result is a challenger, never packed)
#   ADDRESSES         folder of the register-rendered sets (as run.sh). Its dev sets are always part of dev, so epochs are chosen on both layouts; its train
#                     sets are used only when TRAIN_PERMISSIVE=1
#   OSM_COUNT         OpenStreetMap training examples, shared evenly over the country files (default 1000000)
#   ADDRESS_COUNT, BIG_COUNT   register-rendered training examples (the Tier 2 files share ADDRESS_COUNT; us, ca and ust get BIG_COUNT each)
#   NAMES_US_CA, NAMES_TIER2   name sets of the default recipe, a few examples of each (NAMES_COUNT, default 10000, from each), so the name heads exist and the
#                     file loads; they are not scored and play no part in choosing an epoch
#   OUT, BUDGET, THREADS, SEED, EPOCHS   as run.sh
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
O="${OSM:?set OSM}"
A="${ADDRESSES:?set ADDRESSES}"
OUT="${OUT:?set OUT}"
mkdir -p "$OUT"
dll="${RECIPE_DLL:-$here/../../artifacts/bin/OneContact.Recipe.Models/release/RecursiveAI.OneContact.Recipe.Models.dll}"
train=""; dev=""; otrain=""; odev=""
for f in "$A"/*.dev.jsonl; do dev="$dev,$f"; done
for f in "$O"/*.train.jsonl; do otrain="$otrain,$f"; done
for f in "$O"/*.dev.jsonl; do odev="$odev,$f"; done
args=()
if [ "${TRAIN_PERMISSIVE:-0}" = 1 ]; then
  for f in "$A"/*.train.jsonl; do train="$train,$f"; done
  args+=(--addresses "${train#,}" --address-count "${ADDRESS_COUNT:-925000}" --address-big-count "${BIG_COUNT:-80000}")
fi
exec dotnet "$dll" train "${args[@]}" \
  --osm "${otrain#,}" --osm-count "${OSM_COUNT:-1000000}" --osm-dev "${odev#,}" --osm-dev-count "${OSM_DEV_COUNT:-20000}" \
  --names "${NAMES_US_CA:?set NAMES_US_CA}/train" --names-count "${NAMES_COUNT:-10000}" --names2 "${NAMES_TIER2:?set NAMES_TIER2}/train" --names2-count "${NAMES_COUNT:-10000}" \
  --addresses-dev "${dev#,}" --address-big us,ca,ust --address-dev-count 14000 --address-big-dev-count 1500 \
  --seed "${SEED:-1}" --epochs "${EPOCHS:-4}" --threads "${THREADS:-8}" --budget-seconds "${BUDGET:-1500}" \
  --checkpoint-every 500 --out "$OUT"
