#!/usr/bin/env bash
# Fetches the open address registers for France, Belgium, the Netherlands, Luxembourg, Austria, Switzerland, Liechtenstein and North
# Rhine-Westphalia into the data cache, each file checked against its pin (see fetch-register.sh). Licences: provenance/address-tier2-a.md.
# The BAG extract is read member by member out of the 3.6 GB zip by fetch-bag.py (only about 390 MB of it is needed).
set -euo pipefail
here=$(dirname "$0")
for pin in ban best lu at swisstopo de-nrw; do
  "$here/fetch-register.sh" "$here/pins/$pin.pin"
done
python3 "$here/fetch-bag.py" "$here/pins/bag.pin"
