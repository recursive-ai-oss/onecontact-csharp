#!/usr/bin/env bash
# Fetches the pinned GLEIF ELF code list into the upstream cache and checks its SHA-256.
exec "$(dirname "$0")/fetch-pin.sh" "$(dirname "$0")/../../upstream/gleif-elf/PIN" "$@"
