#!/usr/bin/env bash
# Fetches the open address registers of Mexico, Poland, Slovakia and Lithuania into the data cache, each file checked against its
# pin (tools/data/pins/*.pin; same pin format and cache layout as fetch-register.sh). The sites answer a bare curl with a page or a
# refusal, so a browser user agent is sent. The Slovenian register has no file download: fetch-si-rpe.py pages through its public
# WFS and writes the same kind of pinned files (run it once; later runs reuse the pinned copy). Poland, Slovakia and Slovenia are read
# through plain tables written once by "AddressTier2C convert"; Lithuania's geodatabase is converted by convert-lt-gdb.py.
# Licences: provenance/address-tier2-c.md.
set -euo pipefail
here=$(dirname "$0")
agent='Mozilla/5.0 (X11; Linux x86_64; rv:128.0) Gecko/20100101 Firefox/128.0'
for pin in mx pl sk lt; do
  name=$(awk '$1 == "register" { print $2 }' "$here/pins/$pin.pin")
  dest=${ONECONTACT_DATA:-$HOME/onecontact/data}/train/$name
  mkdir -p "$dest"
  while read -r kind file size expected url; do
    [ "$kind" = file ] || continue
    if [ -f "$dest/$file" ] && [ "$(sha256sum "$dest/$file" | cut -d' ' -f1)" = "$expected" ]; then echo "ok       $file"; continue; fi
    curl --fail --silent --show-error --location --retry 3 --proto '=https' -A "$agent" --output "$dest/$file.part" "$url"
    actual=$(sha256sum "$dest/$file.part" | cut -d' ' -f1)
    if [ "$actual" != "$expected" ]; then rm -f "$dest/$file.part"; echo "MISMATCH $file: got $actual, the pin says $expected ($size bytes)" >&2; exit 2; fi
    mv "$dest/$file.part" "$dest/$file"; echo "fetched  $file"
  done < "$here/pins/$pin.pin"
done
