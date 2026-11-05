#!/usr/bin/env bash
# Rebuilds the web set's working files after a re-draw: the page list, the extractor's pre-label and the reviewer view.
#   usage: regen-web.sh <set-dir> <work-dir> <seed> [total]       (needs the built eval-extract tool, EVAL_EXTRACT)
set -euo pipefail
set_dir=$1; work=$2; seed=$3; total=${4:-150}
here=$(cd "$(dirname "$0")" && pwd)
py=${PYTHON:-python3}
mkdir -p "$work"
[ -f "$set_dir/web-picked.jsonl" ] && "$py" -c "
import json
for l in open('$set_dir/web-picked.jsonl'): print(json.loads(l)['id'])" > "$work/kept.txt" || : > "$work/kept.txt"
"$py" "$here/pick-web.py" "$set_dir" "$seed" "$total" "$work/kept.txt"
"$py" -c "
import json
for l in open('$set_dir/web-picked.jsonl'): print(json.loads(l)['id'])" > "$work/ids.txt"
"$EVAL_EXTRACT" ours-web "$set_dir" "$work/pre.jsonl" --ids "$work/ids.txt"
"$EVAL_EXTRACT" review "$set_dir" "$work/pre.jsonl" "$work/review.txt"
