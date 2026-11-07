#!/usr/bin/env bash
# Trains (or resumes) the address-field head over the frozen multilingual encoder for at most BUDGET seconds. Run it again
# with the same arguments to continue from the last checkpoint; the result equals one uninterrupted run.
#   ADDRESSES     folder with <cc>.train.jsonl and <cc>.dev.jsonl for the United States (us), Canada (ca), the territories (ust) and every Tier 2 country
#   OSM           optional: folder with the converted OpenStreetMap sets <cc>.train.jsonl / <cc>.dev.jsonl (ODbL; credited in the package notices)
#   OUT           checkpoint and model folder (the result is OUT/addr.onenlp)
#   BUDGET, THREADS, SEED, EPOCHS, ADDRESS_COUNT, BIG_COUNT, OSM_COUNT,    see Program.cs
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
A="${ADDRESSES:?set ADDRESSES}"
OUT="${OUT:?set OUT}"
mkdir -p "$OUT"
dll="${RECIPE_DLL:-$here/../../artifacts/bin/OneContact.Recipe.Accurate/release/RecursiveAI.OneContact.Recipe.Accurate.dll}"
train=""; dev=""; otrain=""; odev=""
for f in "$A"/*.train.jsonl; do train="$train,$f"; done
for f in "$A"/*.dev.jsonl; do dev="$dev,$f"; done
osm=()
if [ -n "${OSM:-}" ]; then
  for f in "$OSM"/*.train.jsonl; do otrain="$otrain,$f"; done
  for f in "$OSM"/*.dev.jsonl; do odev="$odev,$f"; done
  osm=(--osm "${otrain#,}" --osm-dev "${odev#,}" --osm-count "${OSM_COUNT:-120000}" --osm-dev-count 6000)
fi
exec dotnet "$dll" addresses \
  --addresses "${train#,}" --addresses-dev "${dev#,}" --address-big us,ca,ust \
  --address-count "${ADDRESS_COUNT:-200000}" --address-big-count "${BIG_COUNT:-20000}" \
  --address-dev-count 7000 --address-big-dev-count 750 "${osm[@]}" \
  --seed "${SEED:-1}" --epochs "${EPOCHS:-6}" --threads "${THREADS:-8}" --budget-seconds "${BUDGET:-1500}" \
  --checkpoint-every 500 --out "$OUT"
