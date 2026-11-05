#!/usr/bin/env bash
# Fetches the pinned libphonenumber carrier name data files (see upstream/libphonenumber-carrier/PIN) into the upstream cache.
exec "$(dirname "$0")/fetch-pin.sh" "$(dirname "$0")/../../upstream/libphonenumber-carrier/PIN" "$@"
