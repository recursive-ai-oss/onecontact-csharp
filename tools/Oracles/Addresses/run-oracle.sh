#!/usr/bin/env bash
# Compiles the oracle driver against the built reference jar (see build-oracle.sh) and runs it.
#
#   usage: run-oracle.sh <countryinfo.txt> [--format-map] < cases.tsv > results.tsv
#
# Run on the machine that holds the oracle install. The class files go beside the install, not in the repository.
set -euo pipefail

here=$(cd "$(dirname "$0")" && pwd)
oracle=${ORACLE_HOME_DIR:-$HOME/onecontact/oracles/libaddressinput/81eb9628}
jdk=${JAVA_HOME:-$HOME/onecontact/oracles/jdk/current}
json=${ORACLE_JSON_JAR:-$HOME/onecontact/oracles/deps/json-20240303.jar}
cp="$oracle/common.jar:$json"
classes=$oracle/driver

if [ ! -f "$classes/AddressOracle.class" ] || [ "$here/AddressOracle.java" -nt "$classes/AddressOracle.class" ]; then
  mkdir -p "$classes"
  "$jdk/bin/javac" -nowarn -encoding UTF-8 -cp "$cp" -d "$classes" "$here/AddressOracle.java"
fi
exec "$jdk/bin/java" -Dfile.encoding=UTF-8 -cp "$cp:$classes" AddressOracle "$@"
