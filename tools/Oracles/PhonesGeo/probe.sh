#!/usr/bin/env bash
# Runs GeoProbe.java over the lines on stdin with the pinned jars (see tools/Oracles/PhonesGeo/README.md).
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
oracles=${ONECONTACT_ORACLES:-$HOME/onecontact/oracles}
v=${LIBPHONENUMBER_VERSION:-9.0.43}
d=$oracles/libphonenumber/$v
exec "$oracles/jdk/current/bin/java" -cp "$d/libphonenumber-$v.jar:$d/geocoder-3.43.jar:$d/carrier-2.43.jar:$d/prefixmapper-3.43.jar" "$here/GeoProbe.java"
