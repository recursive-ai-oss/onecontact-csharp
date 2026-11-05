#!/usr/bin/env bash
# Fetches the US DOT National Address Database as the comma-delimited text file (about 8.5 GB zipped). The publisher replaces
# the file in place without a version in the URL, so the pin is the SHA-256 in pins/nad.pin; a newer release fails the check
# until the pin and its provenance entry are updated on purpose.
exec "$(dirname "$0")/fetch-register.sh" "$(dirname "$0")/pins/nad.pin" "$@"
