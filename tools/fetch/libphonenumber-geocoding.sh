#!/usr/bin/env bash
# Fetches the pinned libphonenumber geocoding (area description) data files (see upstream/libphonenumber-geocoding/PIN) into the upstream cache.
exec "$(dirname "$0")/fetch-pin.sh" "$(dirname "$0")/../../upstream/libphonenumber-geocoding/PIN" "$@"
