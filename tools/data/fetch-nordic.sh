#!/usr/bin/env bash
# Fetches the registers of Finland and Iceland and the GeoNames postal-code lists of Denmark, Sweden, Finland, Iceland and Aland into the
# data cache, each file checked against its pin (tools/data/pins/*.pin; same pin format and cache layout as fetch-tier2c.sh).
# Licences: provenance/address-tier2-c.md.
set -euo pipefail
here=$(dirname "$0")
for pin in fi is nordic; do
  name=$(awk '$1 == "register" { print $2 }' "$here/pins/$pin.pin")
  dest=${ONECONTACT_DATA:-$HOME/onecontact/data}/train/$name
  mkdir -p "$dest"
  while read -r kind file size expected url; do
    [ "$kind" = file ] || continue
    if [ -f "$dest/$file" ] && [ "$(sha256sum "$dest/$file" | cut -d' ' -f1)" = "$expected" ]; then echo "ok       $file"; continue; fi
    curl --fail --silent --show-error --location --retry 3 --output "$dest/$file.part" "$url"
    actual=$(sha256sum "$dest/$file.part" | cut -d' ' -f1)
    if [ "$actual" != "$expected" ]; then rm -f "$dest/$file.part"; echo "MISMATCH $file: got $actual, the pin says $expected ($size bytes)" >&2; exit 2; fi
    mv "$dest/$file.part" "$dest/$file"; echo "fetched  $file"
  done < "$here/pins/$pin.pin"
done
