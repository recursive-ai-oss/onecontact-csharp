#!/usr/bin/env bash
# Retrains the name-part and name-class heads of a trained pipeline for at most BUDGET seconds (encoder frozen; the address and contact-span heads
# are not touched). Run it again with the same arguments to continue from the last checkpoint.
#   BASE          the trained pipeline (models.onenlp from run.sh and spans.sh)
#   NAMES_US_CA   folder with train/ and dev/ of the United States and Canada name set (tools/data/names)
#   NAMES_TIER2   folder with train/ and dev/ of the Tier 2 person set (tools/data/names, render-tier2)
#   COMPANIES     folder with train/ and dev/ of the Tier 2 company examples (tools/data/names-tier2, company lines only)
#   OUT           checkpoint and model folder; BUDGET, THREADS, SEED, EPOCHS, NAMES_COUNT, NAMES2_COUNT, NAMES3_COUNT as in NameRecipe.cs
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
dll="${RECIPE_DLL:-$here/../../artifacts/bin/OneContact.Recipe.Models/release/RecursiveAI.OneContact.Recipe.Models.dll}"
mkdir -p "$OUT"
exec dotnet "$dll" names --base "${BASE:?set BASE}" \
  --names "${NAMES_US_CA:?set NAMES_US_CA}/train" --names-dev "$NAMES_US_CA/dev" --names-count "${NAMES_COUNT:-250000}" --names-dev-count 4000 \
  --names2 "${NAMES_TIER2:?set NAMES_TIER2}/train" --names2-dev "$NAMES_TIER2/dev" --names2-count "${NAMES2_COUNT:-450000}" --names2-dev-count 8000 \
  --names3 "${COMPANIES:?set COMPANIES}/train" --names3-dev "$COMPANIES/dev" --names3-count "${NAMES3_COUNT:-130000}" --names3-dev-count 3000 \
  --seed "${SEED:-1}" --epochs "${EPOCHS:-4}" --threads "${THREADS:-8}" --budget-seconds "${BUDGET:-1500}" \
  --checkpoint-every 500 --out "$OUT"
