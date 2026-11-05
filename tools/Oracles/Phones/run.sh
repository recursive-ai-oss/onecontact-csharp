#!/usr/bin/env bash
# Runs the whole phone parity comparison into a work directory:
#   run.sh <work dir> [seed]
# Needs the .NET SDK on PATH, a JDK and the pinned libphonenumber jar (ONECONTACT_ORACLES, default ~/onecontact/oracles, laid out as
# jdk/current and libphonenumber/<version>/libphonenumber-<version>.jar). The restore of the solution must have been done.
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
work=${1:?usage: run.sh <work dir> [seed]}
seed=${2:-1}
version=${LIBPHONENUMBER_VERSION:-9.0.43}
oracles=${ONECONTACT_ORACLES:-$HOME/onecontact/oracles}
java=$oracles/jdk/current/bin/java
jar=$oracles/libphonenumber/$version/libphonenumber-$version.jar
mkdir -p "$work"
tool=(dotnet run --no-restore -c Release --project "$here/OneContact.Oracles.Phones.csproj" --)
if [ -z "${REUSE_CASES:-}" ] || [ ! -f "$work/cases.tsv" ]; then
  "${tool[@]}" generate "$work/cases.tsv" --seed "$seed"
fi
"$java" -cp "$jar" "$here/Driver.java" "$work/cases.tsv" "$work/jar.tsv" 2> "$work/jar.log" &
javapid=$!
"${tool[@]}" ours "$work/cases.tsv" "$work/ours.tsv"
wait $javapid || { echo "the jar driver failed"; cat "$work/jar.log"; exit 1; }
grep -v WARNING "$work/jar.log" | grep -v PhoneNumberUtil || true
"${tool[@]}" diff "$work/ours.tsv" "$work/jar.tsv" "$work/cases.tsv" --show "${SHOW:-5}" --report "$work/diff.txt" || true
head -${SHOW_LINES:-60} "$work/diff.txt"
