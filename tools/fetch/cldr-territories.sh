#!/usr/bin/env bash
# Fetches the pinned CLDR territory names files (see upstream/cldr-territories/PIN) into the upstream cache.
exec "$(dirname "$0")/fetch-pin.sh" "$(dirname "$0")/../../upstream/cldr-territories/PIN" "$@"
