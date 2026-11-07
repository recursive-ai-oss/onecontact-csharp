#!/usr/bin/env bash
# Checks the frozen fresh address sets against every older evaluation store: prints, per country, how many gold pages have a host or URL found
# in the older stores or in another new set (the answer must be 0 everywhere), then the total.
# usage: check-addresses.sh <set root> <private root> <CC> [<CC> ...]
# <set root> holds sets/gold-address-<CC>.jsonl and the page stores written by draw-addresses.sh (<CC>/pages.jsonl, t2/pages.jsonl, and any
# second draw under <set root>/*/<CC>/pages.jsonl). Each country gets a small folder of links, <set root>/check-<CC>; exclude.py skips the folder
# that holds the checked sets (the whole set root), so the new sets' own stores are not counted as older ones. Host lists of other users of the
# same crawl go into a folder outside the set root beforehand (any file named exclude-hosts*.txt under the private root counts as an older store).
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
root=$(cd "${1:?set root}" && pwd); private=${2:?private root}; shift 2
rm -rf "$root"/check-*
dirs=()
for c in "$@"; do
  d=$root/check-$c; mkdir -p "$d"
  ln -s "$root/sets/gold-address-$c.jsonl" "$d/gold-address-$c.jsonl"
  n=0
  for p in "$root/$c/pages.jsonl" "$root/t2/pages.jsonl" "$root"/*/"$c"/pages.jsonl; do
    [ -f "$p" ] && { ln -s "$p" "$d/pages$n.jsonl"; n=$((n+1)); }
  done
  dirs+=("$d")
done
python3 "$here/exclude.py" check "$private" "${dirs[@]}"
