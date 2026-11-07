#!/usr/bin/env bash
# Fetches the open address registers of Norway, the Czech Republic, Latvia and Estonia into the data cache, each file checked against
# its pin (tools/data/pins/*.pin; same pin format and cache layout as fetch-register.sh). The Estonian extract is published on a
# public SFTP server with a read-only login the Land and Spatial Board publishes in its readme.pdf; give it as user:password in
# ONECONTACT_EE_SFTP_LOGIN (kept out of the pin so it is not mistaken for a secret). The others are plain HTTPS. Licences:
# provenance/address-tier2-c.md.
set -euo pipefail
here=$(dirname "$0")
for pin in no cz lv ee; do
  name=$(awk '$1 == "register" { print $2 }' "$here/pins/$pin.pin")
  dest=${ONECONTACT_DATA:-$HOME/onecontact/data}/train/$name
  mkdir -p "$dest"
  while read -r kind file size expected url; do
    [ "$kind" = file ] || continue
    if [ -f "$dest/$file" ] && [ "$(sha256sum "$dest/$file" | cut -d' ' -f1)" = "$expected" ]; then echo "ok       $file"; continue; fi
    opts=(--fail --silent --show-error --location --retry 3)
    case "$url" in
      sftp://*)
        [ -n "${ONECONTACT_EE_SFTP_LOGIN:-}" ] || { echo "set ONECONTACT_EE_SFTP_LOGIN to the read-only login published in the Land and Spatial Board's readme.pdf" >&2; exit 2; }
        opts+=(--proto '=sftp' -k --user "$ONECONTACT_EE_SFTP_LOGIN") ;;
      *) opts+=(--proto '=https') ;;
    esac
    curl "${opts[@]}" --output "$dest/$file.part" "$url"
    actual=$(sha256sum "$dest/$file.part" | cut -d' ' -f1)
    if [ "$actual" != "$expected" ]; then rm -f "$dest/$file.part"; echo "MISMATCH $file: got $actual, the pin says $expected ($size bytes)" >&2; exit 2; fi
    mv "$dest/$file.part" "$dest/$file"; echo "fetched  $file"
  done < "$here/pins/$pin.pin"
done
