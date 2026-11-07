#!/usr/bin/env bash
# Renders the Tier 2 contact blocks that the contact-span head of the models package learns from, with tools/data/blocks (render-tier2).
#   ONECONTACT_DATA   folder with names-tier2-v1/ (tools/data/names-tier2) and the rendered Tier 2 address sets, one folder per group:
#                     ADDRESS_SETS (space-separated folders holding <cc>.train.jsonl and <cc>.dev.jsonl)
#   OUT               output folder (train/ and dev/ inside)
#   COUNT, SEED, PER_FILE   see tools/data/blocks/Program.cs (defaults 400000, 20261108, 6000)
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
D="${ONECONTACT_DATA:-$HOME/onecontact/data/train}"
dll="${BLOCKS_DLL:-$here/../../artifacts/bin/OneContact.Data.Blocks/release/RecursiveAI.OneContact.Data.Blocks.dll}"
files=""
for dir in $ADDRESS_SETS; do
  for f in "$dir"/*.train.jsonl "$dir"/*.dev.jsonl; do
    [ -e "$f" ] || continue
    case "$(basename "$f")" in us.*|ca.*|ust.*) continue ;; esac
    files="$files,$f"
  done
done
exec dotnet "$dll" render-tier2 --names "$D/names-tier2-v1" --addr-files "${files#,}" \
  --out "$OUT" --seed "${SEED:-20261108}" --count "${COUNT:-400000}" --per-file "${PER_FILE:-6000}"
