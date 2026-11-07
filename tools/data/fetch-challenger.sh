#!/usr/bin/env bash
# Fetches the OpenStreetMap address file of the published libpostal parser training set into the data cache, plus the licence texts
# that go beside it. Challenger data: it is kept apart from the permissive registers and never feeds the default models.
#
#   usage: fetch-challenger.sh [pin file]            (default: pins/challenger-osm.pin)
#
# The file lands in $ONECONTACT_DATA/challenger/osm (default ~/onecontact/data/challenger/osm). Every file is checked against the
# SHA-1 the host publishes and the SHA-256 in the pin. RECORD=1 records the SHA-256 of a file whose pin still has 64 zeros.
# The OpenAddresses, GeoPlanet, places and ways files of the same item are deliberately not fetched (see
# provenance/address-challenger-osm.md).
set -euo pipefail

here=$(dirname "$0")
pin=${1:-$here/pins/challenger-osm.pin}
dest=${ONECONTACT_DATA:-$HOME/onecontact/data}/challenger/osm
mkdir -p "$dest"

sha256_of() { if command -v sha256sum >/dev/null 2>&1; then sha256sum "$1" | cut -d' ' -f1; else shasum -a 256 "$1" | cut -d' ' -f1; fi; }
sha1_of() { if command -v sha1sum >/dev/null 2>&1; then sha1sum "$1" | cut -d' ' -f1; else shasum -a 1 "$1" | cut -d' ' -f1; fi; }

# Licence texts, fetched verbatim and kept beside the data.
fetch_text() {
  local name=$1 url=$2
  [ -s "$dest/$name" ] || curl --fail --silent --show-error --location --proto '=https' --retry 3 --output "$dest/$name" "$url"
  echo "text     $name $(sha256_of "$dest/$name")"
}
fetch_text LICENSE-ODbL-1.0.txt https://opendatacommons.org/licenses/odbl/odbl-10.txt
fetch_text OSM-copyright.html https://www.openstreetmap.org/copyright
cat > "$dest/ATTRIBUTION.txt" <<'EOF'
Contains information from OpenStreetMap, which is made available under the Open Database License (ODbL).
(c) OpenStreetMap contributors, ODbL 1.0: https://www.openstreetmap.org/copyright
File: formatted_addresses_tagged.random.tsv.gz of the libpostal parser training data (2017-03-04),
https://archive.org/details/libpostal-parser-training-data-20170304
EOF

status=0
while read -r kind file size expected url sha1; do
  [ "$kind" = file ] || continue
  target=$dest/$file
  if [ -f "$target" ] && [ "$(sha256_of "$target")" = "$expected" ]; then
    echo "ok       $file"
    continue
  fi
  # The host limits each connection to a few hundred kB/s, so the file is fetched as byte-range pieces, several at a time, each piece
  # resumable; the pieces are joined and the result is checked as a whole.
  parts=$target.pieces
  mkdir -p "$parts"
  piece=268435456
  count=$(( (size + piece - 1) / piece ))
  fetch_piece() {
    local i=$1 start end want have tries=0
    start=$(( i * piece )); end=$(( start + piece - 1 )); [ "$end" -ge "$size" ] && end=$(( size - 1 ))
    want=$(( end - start + 1 ))
    while :; do
      have=0; [ -f "$parts/$i" ] && have=$(wc -c < "$parts/$i")
      [ "$have" -ge "$want" ] && return 0
      tries=$(( tries + 1 )); [ "$tries" -gt 40 ] && return 1
      curl --fail --silent --show-error --location --proto '=https' -r "$(( start + have ))-$end" "$url" >> "$parts/$i" || sleep 5
    done
  }
  export -f fetch_piece; export parts piece size url
  seq 0 $(( count - 1 )) | xargs -P "${CONNECTIONS:-12}" -I{} bash -c 'fetch_piece {}'
  : > "$target.part"
  for i in $(seq 0 $(( count - 1 ))); do cat "$parts/$i" >> "$target.part"; done
  rm -rf "$parts"
  actual1=$(sha1_of "$target.part")
  actual=$(sha256_of "$target.part")
  bytes=$(wc -c < "$target.part" | tr -d ' ')
  if [ "$actual1" != "$sha1" ] || [ "$bytes" != "$size" ]; then
    rm -f "$target.part"
    echo "MISMATCH $file: sha1 $actual1 ($bytes bytes), the host publishes $sha1 ($size bytes)" >&2
    status=2
    continue
  fi
  if [ "$actual" != "$expected" ]; then
    if [ "${RECORD:-0}" = 1 ]; then
      mv "$target.part" "$target"
      echo "file $file $bytes $actual $url $sha1"
      continue
    fi
    rm -f "$target.part"
    echo "MISMATCH $file: got $actual, the pin says $expected" >&2
    status=2
    continue
  fi
  mv "$target.part" "$target"
  echo "fetched  $file"
done < "$pin"
exit "$status"
