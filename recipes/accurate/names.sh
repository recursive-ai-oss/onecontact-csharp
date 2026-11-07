#!/usr/bin/env bash
# Adds the name-part and name-class heads to the trained address head, for at most BUDGET seconds; run again with the same arguments to continue.
#   BASE          the address pipeline (addr.onenlp from run.sh)
#   NAMES_US_CA, NAMES_TIER2, COMPANIES   folders with train/ and dev/ of the name sets
#   OUT           checkpoint and model folder (the result is OUT/heads.onenlp); BUDGET, THREADS, SEED, EPOCHS, NAMES_COUNT, NAMES2_COUNT, NAMES3_COUNT as in Program.cs
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
dll="${RECIPE_DLL:-$here/../../artifacts/bin/OneContact.Recipe.Accurate/release/RecursiveAI.OneContact.Recipe.Accurate.dll}"
mkdir -p "${OUT:?set OUT}"
exec dotnet "$dll" names --base "${BASE:?set BASE}" \
  --names "${NAMES_US_CA:?set NAMES_US_CA}/train" --names-dev "$NAMES_US_CA/dev" --names-count "${NAMES_COUNT:-40000}" --names-dev-count 2000 \
  --names2 "${NAMES_TIER2:?set NAMES_TIER2}/train" --names2-dev "$NAMES_TIER2/dev" --names2-count "${NAMES2_COUNT:-60000}" --names2-dev-count 2000 \
  --names3 "${COMPANIES:?set COMPANIES}/train" --names3-dev "$COMPANIES/dev" --names3-count "${NAMES3_COUNT:-30000}" --names3-dev-count 1500 \
  --seed "${SEED:-1}" --epochs "${EPOCHS:-6}" --threads "${THREADS:-8}" --budget-seconds "${BUDGET:-1500}" \
  --checkpoint-every 500 --out "$OUT"
