#!/usr/bin/env bash
# Draws and fetches the crawled pages the span head learns from: pages of one Common Crawl crawl whose site is none of the evaluation
# sets' sites. One crawler process, two fetch threads, back-off on 403/429 (tools/Eval/crawl/cc.py).
#
#   usage: draw.sh <crawl-id> <seed> <blocks-us> <blocks-ca> <work dir>
#   environment: ONECONTACT_EVAL (default ~/onecontact/eval/private) is walked for the hosts to leave out.
#
# Steps: exclusions.py (hosts and sites of every evaluation set), select.py (random cluster.idx blocks with the seed, HTML captures with
# status 200, at most two per host per block), check-overlap.py filter (drops excluded sites), fetch.py (WARC byte ranges, body stored
# unchanged). Page text stays in <work dir> on this machine; nothing is uploaded.
set -euo pipefail
crawl=$1; seed=$2; nus=$3; nca=$4; work=$5
here=$(cd "$(dirname "$0")" && pwd)
eval_root=${ONECONTACT_EVAL:-$HOME/onecontact/eval/private}
mkdir -p "$work"
python3 "$here/exclusions.py" "$eval_root" "$work/excl" ${EXTRA_EXCLUDE:-}
python3 "$here/../../Eval/extraction/select.py" "$crawl" "$work" "$seed" web "$nus" "$nca" --exclude "$work/excl"
mv "$work/candidates.jsonl" "$work/candidates.all.jsonl"
python3 "$here/check-overlap.py" filter "$work/excl" "$work/candidates.all.jsonl" "$work/candidates.jsonl"
python3 "$here/../../Eval/extraction/fetch.py" "$work" "${MAX_PAGES:-100000}" 2
python3 "$here/check-overlap.py" check "$work/excl" "$work/pages.jsonl"
