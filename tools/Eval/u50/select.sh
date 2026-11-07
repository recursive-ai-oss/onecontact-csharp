#!/usr/bin/env bash
# Draws the candidate pages of the fresh United States and Canada name sets, leaving out every host already used by any evaluation set.
# usage: select.sh <private root> <work dir> <seed> <us blocks> <ca blocks>    (run once; then fetch-pages.py <work dir> <max> 2)
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
root=${1:?private root}; work=${2:?work dir}; seed=${3:?seed}; us=${4:?}; ca=${5:?}
mkdir -p "$work"
python3 "$here/exclude.py" build "$root" "$work/exclude"
EXCLUDE_HOSTS=$work/exclude-hosts.txt python3 "$here/../crawl/select-pages.py" "${CRAWL:-CC-MAIN-2026-43}" "$work" "$seed" "$us" "$ca" balanced
