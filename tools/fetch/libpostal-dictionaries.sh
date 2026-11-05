#!/usr/bin/env bash
# Fetches the libpostal dictionary files named in upstream/libpostal-dictionaries/PIN (data files only) at the pinned tag.
exec "$(dirname "$0")/fetch-pin.sh" "$(dirname "$0")/../../upstream/libpostal-dictionaries/PIN" "$@"
