#!/usr/bin/env bash
# Runs LocaleScan.java (tags on stdin) with the pinned JDK.
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
oracles=${ONECONTACT_ORACLES:-$HOME/onecontact/oracles}
exec "$oracles/jdk/current/bin/java" "$here/LocaleScan.java"
