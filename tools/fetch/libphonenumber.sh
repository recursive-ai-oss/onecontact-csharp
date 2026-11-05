#!/usr/bin/env bash
# Fetches the pinned libphonenumber data files (see upstream/libphonenumber/PIN) into the upstream cache.
exec "$(dirname "$0")/fetch-pin.sh" "$(dirname "$0")/../../upstream/libphonenumber/PIN" "$@"
