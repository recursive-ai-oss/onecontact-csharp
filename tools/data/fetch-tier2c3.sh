#!/usr/bin/env bash
# Fetches the sources of Croatia, Hungary, Romania and Australia into the data cache, each file checked against its pin
# (tools/data/pins/{hr,hu,ro,au}.pin; same pin format and cache layout as fetch-register.sh). The Romanian workbook is converted to
# tab-separated text by ro-infocod-to-tsv.py, and the converted files are checked against the `derived` lines of ro.pin.
# Licences: provenance/address-tier2-c.md.
set -euo pipefail
here=$(dirname "$0")
root=${ONECONTACT_DATA:-$HOME/onecontact/data}/train
for pin in hr hu ro au; do
  name=$(awk '$1 == "register" { print $2 }' "$here/pins/$pin.pin")
  dest=$root/$name
  mkdir -p "$dest"
  while read -r kind file size expected url; do
    [ "$kind" = file ] || continue
    if [ -f "$dest/$file" ] && [ "$(sha256sum "$dest/$file" | cut -d' ' -f1)" = "$expected" ]; then echo "ok       $file"; continue; fi
    curl --fail --silent --show-error --location --retry 3 --proto '=https' --output "$dest/$file.part" "$url"
    actual=$(sha256sum "$dest/$file.part" | cut -d' ' -f1)
    if [ "$actual" != "$expected" ]; then rm -f "$dest/$file.part"; echo "MISMATCH $file: got $actual, the pin says $expected ($size bytes)" >&2; exit 2; fi
    mv "$dest/$file.part" "$dest/$file"; echo "fetched  $file"
  done < "$here/pins/$pin.pin"
done

# Romania: convert the workbook, then check the derived files.
ro=$root/$(awk '$1 == "register" { print $2 }' "$here/pins/ro.pin")
workbook=$(awk '$1 == "file" { print $2; exit }' "$here/pins/ro.pin")
if [ ! -f "$ro/bucuresti.tsv" ]; then
  uv run --no-project --with xlrd==2.0.1 python "$here/ro-infocod-to-tsv.py" "$ro/$workbook" "$ro"
fi
while read -r kind file expected; do
  [ "$kind" = derived ] || continue
  actual=$(sha256sum "$ro/$file" | cut -d' ' -f1)
  if [ "$actual" != "$expected" ]; then echo "MISMATCH $file (converted): got $actual, the pin says $expected" >&2; exit 2; fi
  echo "ok       $file (converted)"
done < "$here/pins/ro.pin"
