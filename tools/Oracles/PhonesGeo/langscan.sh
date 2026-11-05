#!/usr/bin/env bash
# Runs LangScan.java with the pinned jars.
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
oracles=${ONECONTACT_ORACLES:-$HOME/onecontact/oracles}
v=${LIBPHONENUMBER_VERSION:-9.0.43}
d=$oracles/libphonenumber/$v
exec "$oracles/jdk/current/bin/java" -cp "$d/libphonenumber-$v.jar:$d/geocoder-3.43.jar:$d/prefixmapper-3.43.jar" "$here/LangScan.java"
