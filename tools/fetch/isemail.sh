#!/usr/bin/env bash
exec "$(dirname "$0")/fetch-pin.sh" "$(dirname "$0")/../../upstream/isemail/PIN" "$@"
