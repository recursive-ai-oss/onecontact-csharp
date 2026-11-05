#!/usr/bin/env bash
# Runs the whole geo parity comparison into a work directory:
#   run.sh <work dir> [seed] [prefixes per calling code]
# Needs the .NET SDK on PATH, a JDK and the pinned jars (ONECONTACT_ORACLES, default ~/onecontact/oracles, laid out as jdk/current and
# libphonenumber/<version>/{libphonenumber,geocoder,carrier,prefixmapper}-*.jar), and the fetched upstream data (ONECONTACT_UPSTREAM).
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
work=${1:?usage: run.sh <work dir> [seed] [prefixes]}
seed=${2:-1}
prefixes=${3:-200}
v=${LIBPHONENUMBER_VERSION:-9.0.43}
oracles=${ONECONTACT_ORACLES:-$HOME/onecontact/oracles}
d=$oracles/libphonenumber/$v
mkdir -p "$work"
tool=(dotnet run --no-restore -c Release --project "$here/OneContact.Oracles.PhonesGeo.csproj" --)
if [ -z "${REUSE_CASES:-}" ] || [ ! -f "$work/cases.tsv" ]; then
  "${tool[@]}" generate "$work/cases.tsv" --seed "$seed" --prefixes "$prefixes"
fi
"$oracles/jdk/current/bin/java" -cp "$d/libphonenumber-$v.jar:$d/geocoder-3.43.jar:$d/carrier-2.43.jar:$d/prefixmapper-3.43.jar" "$here/GeoDriver.java" "$work/cases.tsv" "$work/jar.tsv" 2> "$work/jar.log" &
javapid=$!
"${tool[@]}" ours "$work/cases.tsv" "$work/ours.tsv"
wait $javapid || { echo "the jar driver failed"; head -20 "$work/jar.log"; exit 1; }
"${tool[@]}" diff "$work/ours.tsv" "$work/jar.tsv" "$work/cases.tsv" --show "${SHOW:-6}" --report "$work/diff.txt" || true
head -"${SHOW_LINES:-70}" "$work/diff.txt" | cut -c1-240
