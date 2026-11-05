#!/usr/bin/env bash
# Fetches the files of one training register into the data cache and checks every SHA-256 against its pin.
#
#   usage: fetch-register.sh <pin file>
#
# A pin file has `register <name>` and one `file <local name> <size in bytes> <sha256> <url>` line per file. The cache is
# $ONECONTACT_DATA/train/<name> (default ~/onecontact/data/train/<name>); registers are large and never go in the repository.
# A file that is already there with the pinned hash is left alone; a hash that differs is an error and the file is removed.
# Run with RECORD=1 to print `file` lines for files that have no hash yet (a pin line with 64 zeros) after you have read the
# licence page; that is how a pin is first made.
set -euo pipefail

pin=${1:?usage: fetch-register.sh <pin file>}
name=$(awk '$1 == "register" { print $2 }' "$pin")
[ -n "$name" ] || { echo "fetch-register: $pin has no register line" >&2; exit 3; }
dest=${ONECONTACT_DATA:-$HOME/onecontact/data}/train/$name
mkdir -p "$dest"

sha256_of() {
  if command -v sha256sum >/dev/null 2>&1; then sha256sum "$1" | cut -d' ' -f1; else shasum -a 256 "$1" | cut -d' ' -f1; fi
}

status=0
while read -r kind file size expected url; do
  [ "$kind" = file ] || continue
  target=$dest/$file
  if [ -f "$target" ] && [ "$(sha256_of "$target")" = "$expected" ]; then
    echo "ok       $file"
    continue
  fi
  curl --fail --silent --show-error --location --proto '=https' --retry 3 --output "$target.part" "$url"
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
