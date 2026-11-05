#!/usr/bin/env bash
# Fetches the pinned libphonenumber time-zone map files (see upstream/libphonenumber-timezones/PIN) into the upstream cache.
exec "$(dirname "$0")/fetch-pin.sh" "$(dirname "$0")/../../upstream/libphonenumber-timezones/PIN" "$@"
