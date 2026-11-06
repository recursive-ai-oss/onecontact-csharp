#!/usr/bin/env bash
# Fetches the sources of the Tier 2 group B training data into the data cache, each file checked against its pin (see fetch-register.sh):
# OS Open Names, the Catastro's municipal address files, the ANNCSU regional address files, the ISTAT list of comuni and the GeoNames postal codes.
# With --record it first builds the Catastro pin from the live feeds and fills in every missing hash (a pin line with 64 zeros) after you have
# read the licence pages; the committed pins already have their hashes.
set -euo pipefail
here=$(dirname "$0")
if [ "${1:-}" = "--record" ]; then
  python3 "$here/make-catastro-pin.py" "$here/pins/catastro.pin" 120
  export RECORD=1
fi
for pin in osopennames geonames anncsu istat catastro; do
  "$here/fetch-register.sh" "$here/pins/$pin.pin"
done
