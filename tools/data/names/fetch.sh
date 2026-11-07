#!/usr/bin/env bash
# Fetches the name lists and company registers of pins/names.pin into the data cache and checks every SHA-256.
#
#   usage: fetch.sh [pin file]
#
# The cache is $ONECONTACT_DATA/train/names (default ~/onecontact/data/train/names); the files are large and never go in the repository.
# A file that is already there with the pinned hash is left alone; a hash that differs is an error and the file is removed.
# The SEC asks scripted clients to identify themselves: set FETCH_USER_AGENT to a string that names you and a contact address.
# Run with RECORD=1 to print `file` lines for files whose hash differs (to make a pin for a newer release after reading its licence page).
#
# A `file` line may end with a JSON request body (no spaces): the file is then fetched with a POST (the statistics offices' query APIs).
# A `licence <dir> <file>` line copies licences/<file> (the publisher's licence wording and page) next to the data of <dir>.
set -euo pipefail

here=$(cd "$(dirname "$0")" && pwd)
pin=${1:-$here/pins/names.pin}
name=$(awk '$1 == "register" { print $2 }' "$pin")
[ -n "$name" ] || { echo "fetch: $pin has no register line" >&2; exit 3; }
dest=${ONECONTACT_DATA:-$HOME/onecontact/data}/train/$name
agent=${FETCH_USER_AGENT:-Mozilla/5.0 (compatible; OneContact-data-fetch)}
mkdir -p "$dest"

sha256_of() {
  if command -v sha256sum >/dev/null 2>&1; then sha256sum "$1" | cut -d' ' -f1; else shasum -a 256 "$1" | cut -d' ' -f1; fi
}

status=0
while read -r kind file size expected url post; do
  if [ "$kind" = licence ]; then
    mkdir -p "$dest/$file" && cp "$here/licences/$size" "$dest/$file/LICENCE.txt"
    continue
  fi
  [ "$kind" = file ] || continue
  target=$dest/$file
  if [ -f "$target" ] && [ "$(sha256_of "$target")" = "$expected" ]; then
    echo "ok       $file"
    continue
  fi
  mkdir -p "$(dirname "$target")"
  # Some publishers refuse one client name and accept another (or answer with a bot-check page), so each file is tried with ours and then with
  # curl's own.
  ok=0
  for ua in "$agent" ""; do
    args=(--fail --silent --show-error --location --retry 3 --output "$target.part")
    [ -n "$ua" ] && args+=(--user-agent "$ua")
    if [ -n "${post:-}" ]; then args+=(-H 'Content-Type: application/json' --max-time 300 --data "$post"); fi
    if curl "${args[@]}" "$url" 2>/dev/null && ! head -c 15 "$target.part" | grep -qiE '^(<!doctype|<html)'; then ok=1; break; fi
  done
  if [ "$ok" != 1 ]; then rm -f "$target.part"; echo "FAILED   $file" >&2; status=2; continue; fi
  actual=$(sha256_of "$target.part")
  bytes=$(wc -c < "$target.part" | tr -d ' ')
  if [ "$actual" != "$expected" ]; then
    if [ "${RECORD:-0}" = 1 ]; then
      mv "$target.part" "$target"
      echo "file $file $bytes $actual $url"
      continue
    fi
    rm -f "$target.part"
    echo "MISMATCH $file: got $actual ($bytes bytes), the pin says $expected ($size bytes)" >&2
    status=2
    continue
  fi
  mv "$target.part" "$target"
  echo "fetched  $file"
done < "$pin"
exit "$status"
