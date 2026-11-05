#!/usr/bin/env bash
# Fetches the files of every pin under upstream/ (see fetch-pin.sh) into $ONECONTACT_UPSTREAM, defaulting to .upstream at the
# repository root. Sources need no registration here: a new upstream/<source>/PIN is picked up by name.
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
status=0
for pin in "$here"/../../upstream/*/PIN; do
  [ -e "$pin" ] || continue
  echo "== $(basename "$(dirname "$pin")")"
  "$here/fetch-pin.sh" "$pin" || status=$?
done
exit "$status"
