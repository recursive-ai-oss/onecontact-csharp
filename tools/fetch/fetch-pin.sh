#!/usr/bin/env bash
# Fetches the exact files a pin names, at the pinned tag or commit, into the upstream cache, and checks every SHA-256.
#
#   usage: fetch-pin.sh <pin file> [destination directory]
#
# The destination defaults to $ONECONTACT_UPSTREAM/<source> (or .upstream/<source> at the repository root, which git ignores).
# Files are fetched one by one by exact path: never a clone, never a source archive. A file that is already there with the right
# hash is left alone. A hash that differs from the pin is an error and the file is not kept.
# Set FETCH_PROTO to override curl's protocol allow-list (the default is https only; tests use file).
set -euo pipefail

pin=${1:?usage: fetch-pin.sh <pin file> [destination directory]}
root=$(cd "$(dirname "$0")/../.." && pwd)
source_name=$(awk '$1 == "source" { print $2 }' "$pin")
urlbase=$(awk '$1 == "urlbase" { print $2 }' "$pin")
[ -n "$source_name" ] && [ -n "$urlbase" ] || { echo "fetch-pin: $pin has no source or urlbase" >&2; exit 3; }
dest=${2:-${ONECONTACT_UPSTREAM:-$root/.upstream}/$source_name}
proto=${FETCH_PROTO:-=https}

sha256_of() {
  if command -v sha256sum >/dev/null 2>&1; then sha256sum "$1" | cut -d' ' -f1; else shasum -a 256 "$1" | cut -d' ' -f1; fi
}

status=0
while read -r path expected; do
  target=$dest/$path
  if [ -f "$target" ] && [ "$(sha256_of "$target")" = "$expected" ]; then
    echo "ok      $path"
    continue
  fi
  mkdir -p "$(dirname "$target")"
  tmp=$target.part
  curl --fail --silent --show-error --location --proto "$proto" --output "$tmp" "$urlbase$path"
  actual=$(sha256_of "$tmp")
  if [ "$actual" != "$expected" ]; then
    rm -f "$tmp"
    echo "MISMATCH $path: got $actual, the pin says $expected" >&2
    status=2
    continue
  fi
  mv "$tmp" "$target"
  echo "fetched $path"
done < <(awk '$1 == "file" { print $2, $3 }' "$pin")
exit "$status"
